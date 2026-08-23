#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=grading-access-common.sh
source "$SCRIPT_DIR/grading-access-common.sh"

SUBSCRIPTION_ID=""
GRADING_PRINCIPAL_ID=""
GRADING_TENANT_ID=""
STUDENT_EMAIL=""
INSTRUCTOR_PRINCIPAL_ID=""
RESOURCE_GROUP="projProd"
ACCESS_MODE="auto"

usage() {
  cat <<'EOF'
Usage:
  offboard-managed-identity.sh \
    -s <student-subscription-id> \
    -p <grading-managed-identity-principal-id> \
    -t <grading-tenant-id> \
    -e <azure-isekai-sign-in-email> \
    [-i <instructor-user-object-id>] \
    [-g <assignment-resource-group>] \
    [-m <auto|direct|lighthouse>]

Removes the direct RBAC assignments or Azure Lighthouse delegations created by
onboard-managed-identity.sh. For direct RBAC, pass the same instructor ID used
for onboarding so its assignments are also removed.
EOF
}

while getopts ":s:p:t:e:i:g:m:h" opt; do
  case "$opt" in
    s) SUBSCRIPTION_ID="$OPTARG" ;;
    p) GRADING_PRINCIPAL_ID="$OPTARG" ;;
    t) GRADING_TENANT_ID="$OPTARG" ;;
    e) STUDENT_EMAIL="${OPTARG,,}" ;;
    i) INSTRUCTOR_PRINCIPAL_ID="$OPTARG" ;;
    g) RESOURCE_GROUP="$OPTARG" ;;
    m) ACCESS_MODE="${OPTARG,,}" ;;
    h) usage; exit 0 ;;
    :) echo "Missing argument for -$OPTARG" >&2; usage; exit 2 ;;
    \?) echo "Invalid option: -$OPTARG" >&2; usage; exit 2 ;;
  esac
done

if [[ -z "$SUBSCRIPTION_ID" || -z "$GRADING_PRINCIPAL_ID" ||
      -z "$GRADING_TENANT_ID" || -z "$STUDENT_EMAIL" ]]; then
  usage
  exit 2
fi

require_guid "Student subscription ID" "$SUBSCRIPTION_ID"
require_guid "Grading principal ID" "$GRADING_PRINCIPAL_ID"
require_guid "Grading tenant ID" "$GRADING_TENANT_ID"
if [[ -n "$INSTRUCTOR_PRINCIPAL_ID" ]]; then
  require_guid "Instructor principal ID" "$INSTRUCTOR_PRINCIPAL_ID"
fi
if [[ "$STUDENT_EMAIL" =~ [[:space:]] ]] || [[ "$STUDENT_EMAIL" != *@* ]]; then
  echo "Student email must be a non-empty email address without whitespace." >&2
  exit 2
fi

az account show --subscription "$SUBSCRIPTION_ID" >/dev/null
STUDENT_TENANT_ID=$(az account show \
  --subscription "$SUBSCRIPTION_ID" \
  --query tenantId \
  -o tsv)
ACCESS_MODE=$(resolve_access_mode \
  "$ACCESS_MODE" \
  "$STUDENT_TENANT_ID" \
  "$GRADING_TENANT_ID")

SUBSCRIPTION_SCOPE="/subscriptions/$SUBSCRIPTION_ID"
RESOURCE_GROUP_SCOPE="$SUBSCRIPTION_SCOPE/resourceGroups/$RESOURCE_GROUP"

remove_direct_assignment() {
  local principal_id="$1"
  local role_id="$2"
  local scope="$3"
  local label="$4"
  local assignment_count

  assignment_count=$(az role assignment list \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$principal_id" \
    --scope "$scope" \
    --query "[?ends_with(roleDefinitionId, '$role_id')] | length(@)" \
    -o tsv)
  if [[ "$assignment_count" == "0" ]]; then
    echo "$label is not assigned at $scope."
    return
  fi

  az role assignment delete \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$principal_id" \
    --role "$role_id" \
    --scope "$scope" \
    --only-show-errors
  echo "Removed $label from $scope."
}

list_lighthouse_assignment_ids() {
  local definition_id="$1"
  local management_url="https://management.azure.com"
  local api_version="2022-10-01"
  local subscription_scope="/subscriptions/$SUBSCRIPTION_ID"
  local resource_group_scope="$subscription_scope/resourceGroups/$RESOURCE_GROUP"
  local query="[?ends_with(properties.registrationDefinitionId, '/$definition_id')].id"

  az rest \
    --method get \
    --url "$management_url$subscription_scope/providers/Microsoft.ManagedServices/registrationAssignments?api-version=$api_version" \
    --query "value$query" \
    --output tsv

  if az group show \
    --subscription "$SUBSCRIPTION_ID" \
    --name "$RESOURCE_GROUP" \
    --output none 2>/dev/null; then
    az rest \
      --method get \
      --url "$management_url$resource_group_scope/providers/Microsoft.ManagedServices/registrationAssignments?api-version=$api_version" \
      --query "value$query" \
      --output tsv
  fi
}

remove_lighthouse_assignments() {
  local definition_id="$1"
  local label="$2"
  local assignment_ids assignment_id

  assignment_ids="$(list_lighthouse_assignment_ids "$definition_id" | sort -u)"
  if [[ -z "$assignment_ids" ]]; then
    echo "$label assignments do not exist."
    return
  fi

  while IFS= read -r assignment_id; do
    [[ -z "$assignment_id" ]] && continue
    az rest \
      --method delete \
      --url "https://management.azure.com$assignment_id?api-version=2022-10-01" \
      --output none
    echo "Removed $label assignment $assignment_id."
  done <<<"$assignment_ids"
}

remove_lighthouse_definition() {
  local definition_id="$1"
  local label="$2"
  local attempt output

  if ! az managedservices definition show \
    --subscription "$SUBSCRIPTION_ID" \
    --definition "$definition_id" \
    --output none 2>/dev/null; then
    echo "$label does not exist."
    return
  fi

  for attempt in {1..10}; do
    if output=$(az managedservices definition delete \
      --subscription "$SUBSCRIPTION_ID" \
      --definition "$definition_id" \
      --yes \
      --only-show-errors \
      --output none 2>&1); then
      echo "Removed $label."
      return
    fi

    if [[ "$output" != *"InvalidRegistrationDefinitionDeleteRequest"* ||
          "$attempt" -eq 10 ]]; then
      echo "$output" >&2
      return 1
    fi
    sleep 3
  done
}

remove_lighthouse_access() {
  local subscription_definition_id resource_group_definition_id

  subscription_definition_id=$(lighthouse_definition_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "subscription" "reader")
  resource_group_definition_id=$(lighthouse_definition_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "resource-group" "$RESOURCE_GROUP")

  remove_lighthouse_assignments "$resource_group_definition_id" "resource-group"
  remove_lighthouse_assignments "$subscription_definition_id" "subscription Reader"
  remove_lighthouse_definition "$resource_group_definition_id" \
    "resource-group definition"
  remove_lighthouse_definition "$subscription_definition_id" \
    "subscription Reader definition"
}

REGISTERED_EMAIL=$(az group show \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  --query "tags.GradingStudentEmail" \
  -o tsv)
if [[ -n "$REGISTERED_EMAIL" && "${REGISTERED_EMAIL,,}" != "$STUDENT_EMAIL" ]]; then
  echo "The resource group is registered to '$REGISTERED_EMAIL', not '$STUDENT_EMAIL'. No access was removed." >&2
  exit 4
fi

if [[ "$ACCESS_MODE" == "direct" ]]; then
  remove_direct_assignment \
    "$GRADING_PRINCIPAL_ID" "$WEBSITE_CONTRIBUTOR_ROLE_ID" \
    "$RESOURCE_GROUP_SCOPE" "Grader Website Contributor"
  remove_direct_assignment \
    "$GRADING_PRINCIPAL_ID" "$READER_ROLE_ID" \
    "$SUBSCRIPTION_SCOPE" "Grader Reader"
  if [[ -n "$INSTRUCTOR_PRINCIPAL_ID" ]]; then
    remove_direct_assignment \
      "$INSTRUCTOR_PRINCIPAL_ID" "$WEBSITE_CONTRIBUTOR_ROLE_ID" \
      "$RESOURCE_GROUP_SCOPE" "Instructor Website Contributor"
    remove_direct_assignment \
      "$INSTRUCTOR_PRINCIPAL_ID" "$READER_ROLE_ID" \
      "$SUBSCRIPTION_SCOPE" "Instructor Reader"
  fi
else
  remove_lighthouse_access
fi

if [[ -n "$REGISTERED_EMAIL" ]]; then
  az tag update \
    --resource-id "$RESOURCE_GROUP_SCOPE" \
    --operation Delete \
    --tags "GradingStudentEmail=$REGISTERED_EMAIL" \
    --only-show-errors \
    --output none
  echo "Removed the Azure Isekai ownership tag."
else
  echo "Azure Isekai ownership tag does not exist."
fi

echo "Offboarding complete. Access mode: $ACCESS_MODE"
