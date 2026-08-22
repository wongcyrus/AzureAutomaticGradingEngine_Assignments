#!/usr/bin/env bash

set -euo pipefail

cd "$(dirname "$0")"

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
if [[ -z "$grading_principal_id" ]]; then
  echo "Error: grading identity output is unavailable." >&2
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

assignment_count="$(az role assignment list \
  --subscription "$subscription_id" \
  --assignee-object-id "$grading_principal_id" \
  --scope "/subscriptions/$subscription_id" \
  --query "[?roleDefinitionName=='Reader'] | length(@)" \
  --output tsv)"
if [[ "$assignment_count" == "0" ]]; then
  echo "Error: the grading identity does not have Reader on this subscription." >&2
  exit 1
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
