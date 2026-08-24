#!/usr/bin/env bash

set -euo pipefail

cd "$(dirname "$0")"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../scripts/grading-access-common.sh
source "$SCRIPT_DIR/../scripts/grading-access-common.sh"

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <student-email> <subscription-id>" >&2
  exit 2
fi

student_email="${1,,}"
subscription_id="$2"
resource_group="projProd"
stack_resource_group="GradingEngineAssignmentResourceGroup"
outputs_file="$(mktemp)"
trap 'rm -f "$outputs_file"' EXIT

for command in az jq npx; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

if [[ ! "$student_email" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]]; then
  echo "Error: invalid student email: $student_email" >&2
  exit 2
fi

if [[ ! "$subscription_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo "Error: invalid subscription ID: $subscription_id" >&2
  exit 2
fi

npx cdktn output AzureAutomaticGradingEngineGrader \
  --skip-synth \
  --outputs-file "$outputs_file" \
  >/dev/null

grading_principal_id="$(jq -r \
  '.AzureAutomaticGradingEngineGrader.grading_identity_principal_id // empty' \
  "$outputs_file")"
grading_tenant_id="$(jq -r \
  '.AzureAutomaticGradingEngineGrader.grading_identity_tenant_id // empty' \
  "$outputs_file")"
if [[ -z "$grading_principal_id" || -z "$grading_tenant_id" ]]; then
  echo "Error: grading identity outputs are unavailable." >&2
  exit 1
fi

registered_email="$(az group show \
  --subscription "$subscription_id" \
  --name "$resource_group" \
  --query 'tags.GradingStudentEmail' \
  --output tsv)"
if [[ "${registered_email,,}" != "$student_email" ]]; then
  echo "Error: $resource_group is tagged for '$registered_email', not '$student_email'." >&2
  exit 1
fi

student_tenant_id="$(az account show \
  --subscription "$subscription_id" \
  --query tenantId \
  --output tsv)"

verify_lighthouse_definition() {
  local definition_id="$1"
  shift
  local definition_json
  local role_id

  if ! definition_json="$(az managedservices definition show \
    --subscription "$subscription_id" \
    --definition "$definition_id" \
    --output json 2>/dev/null)"; then
    return 1
  fi

  for role_id in "$@"; do
    if ! jq -e \
      --arg tenant "$grading_tenant_id" \
      --arg principal "$grading_principal_id" \
      --arg role "$role_id" \
      '
        (.properties.managedByTenantId | ascii_downcase) == ($tenant | ascii_downcase)
        and any(
          .properties.authorizations[];
          (.principalId | ascii_downcase) == ($principal | ascii_downcase)
          and ((.roleDefinitionId | ascii_downcase) | endswith($role | ascii_downcase))
        )
      ' <<<"$definition_json" >/dev/null; then
      return 1
    fi
  done
}

verify_lighthouse_assignment() {
  local assignment_id="$1"
  local definition_id="$2"
  local resource_group_name="$3"
  local actual_definition
  local expected_definition
  local -a scope_args=()

  if [[ -n "$resource_group_name" ]]; then
    scope_args=(--resource-group "$resource_group_name")
  fi
  if ! actual_definition="$(az managedservices assignment show \
    --subscription "$subscription_id" \
    "${scope_args[@]}" \
    --assignment "$assignment_id" \
    --query properties.registrationDefinitionId \
    --output tsv 2>/dev/null)"; then
    return 1
  fi

  expected_definition="/subscriptions/$subscription_id/providers/Microsoft.ManagedServices/registrationDefinitions/$definition_id"
  [[ "${actual_definition,,}" == "${expected_definition,,}" ]]
}

if [[ "${student_tenant_id,,}" == "${grading_tenant_id,,}" ]]; then
  assignment_count="$(az role assignment list \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --scope "/subscriptions/$subscription_id" \
    --query "[?roleDefinitionName=='Reader'] | length(@)" \
    --output tsv)"
  if [[ "$assignment_count" == "0" ]]; then
    echo "Error: the grading identity does not have direct Reader access on this subscription." >&2
    exit 1
  fi
else
  subscription_definition_id="$(lighthouse_definition_id \
    "$subscription_id" "$grading_tenant_id" "$grading_principal_id" \
    "subscription" "reader")"
  subscription_assignment_id="$(lighthouse_assignment_id \
    "$subscription_id" "$grading_tenant_id" "$grading_principal_id" \
    "subscription" "reader")"
  resource_group_definition_id="$(lighthouse_definition_id \
    "$subscription_id" "$grading_tenant_id" "$grading_principal_id" \
    "resource-group" "$resource_group")"
  resource_group_assignment_id="$(lighthouse_assignment_id \
    "$subscription_id" "$grading_tenant_id" "$grading_principal_id" \
    "resource-group" "$resource_group")"

  if ! verify_lighthouse_definition \
      "$subscription_definition_id" "$READER_ROLE_ID" ||
    ! verify_lighthouse_assignment \
      "$subscription_assignment_id" "$subscription_definition_id" "" ||
    ! verify_lighthouse_definition \
      "$resource_group_definition_id" \
      "$READER_ROLE_ID" "$WEBSITE_CONTRIBUTOR_ROLE_ID" ||
    ! verify_lighthouse_assignment \
      "$resource_group_assignment_id" "$resource_group_definition_id" "$resource_group"; then
    echo "Error: the expected Azure Lighthouse grader delegation is incomplete." >&2
    exit 1
  fi
fi

storage_account="$(az storage account list \
  --resource-group "$stack_resource_group" \
  --query '[0].name' \
  --output tsv)"
if [[ -z "$storage_account" ]]; then
  echo "Error: grading storage account not found." >&2
  exit 1
fi

existing_subscription="$(az storage entity show \
  --account-name "$storage_account" \
  --auth-mode key \
  --table-name Subscription \
  --partition-key "$student_email" \
  --row-key registration \
  --query SubscriptionId \
  --output tsv \
  2>/dev/null || true)"

if [[ -n "$existing_subscription" ]]; then
  if [[ "${existing_subscription,,}" == "${subscription_id,,}" ]]; then
    echo "$student_email is already registered for $subscription_id."
    exit 0
  fi

  echo "Error: $student_email is already registered for $existing_subscription." >&2
  exit 1
fi

az storage entity insert \
  --account-name "$storage_account" \
  --auth-mode key \
  --table-name Subscription \
  --if-exists fail \
  --entity \
    "PartitionKey=$student_email" \
    "RowKey=registration" \
    "SubscriptionId=$subscription_id" \
  --output none

echo "Imported $student_email -> $subscription_id."
