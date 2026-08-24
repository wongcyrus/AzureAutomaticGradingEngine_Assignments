#!/usr/bin/env bash

set -uo pipefail

grading_principal_id="078c7abf-66ed-409c-9e40-e8fdb6a93221"
grading_tenant_id="8ff7db19-435d-4c3c-83d3-ca0a46234f51"
reader_role_id="acdd72a7-3385-48ef-bd42-f606fba81ae7"
contributor_role_id="b24988ac-6180-42a0-ab88-20f7382dd24c"
resource_group="projProd"
api_version="2022-10-01"
management_url="https://management.azure.com"
errors=0

if [[ $# -ne 0 ]]; then
  echo "Usage: bash" >&2
  exit 2
fi

for command in az jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

account_json="$(az account show --output json)"
subscription_id="$(jq -r '.id' <<<"$account_json")"
subscription_name="$(jq -r '.name' <<<"$account_json")"
student_tenant_id="$(jq -r '.tenantId | ascii_downcase' <<<"$account_json")"
student_email="$(jq -r '.user.name | ascii_downcase' <<<"$account_json")"
subscription_scope="/subscriptions/$subscription_id"
resource_group_scope="$subscription_scope/resourceGroups/$resource_group"

if [[ "$student_tenant_id" == "$grading_tenant_id" ]]; then
  expected_mode="direct"
else
  expected_mode="lighthouse"
fi

echo "Azure Isekai grading-access diagnostics"
echo "Subscription: $subscription_name ($subscription_id)"
echo "Cloud Shell identity: $student_email"
echo "Subscription tenant: $student_tenant_id"
echo "Grading tenant: $grading_tenant_id"
echo "Grader principal: $grading_principal_id"
echo "Expected access mode: $expected_mode"

if ! az group show \
  --subscription "$subscription_id" \
  --name "$resource_group" \
  --only-show-errors \
  --output none; then
  echo "FAIL: resource group '$resource_group' does not exist."
  errors=$((errors + 1))
else
  registered_email="$(
    az group show \
      --subscription "$subscription_id" \
      --name "$resource_group" \
      --query "tags.GradingStudentEmail" \
      --output tsv
  )"
  if [[ "${registered_email,,}" == "$student_email" ]]; then
    echo "PASS: resource-group ownership tag matches $student_email."
  else
    echo "FAIL: resource-group ownership tag is '${registered_email:-missing}', expected '$student_email'."
    errors=$((errors + 1))
  fi
fi

direct_assignment_count() {
  local scope="$1"
  local role_id="$2"

  az role assignment list \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --scope "$scope" \
    --query "[?ends_with(roleDefinitionId, '/$role_id') && scope == '$scope'] | length(@)" \
    --output tsv 2>/dev/null || echo 0
}

subscription_direct_count="$(
  direct_assignment_count "$subscription_scope" "$reader_role_id"
)"
resource_group_direct_count="$(
  direct_assignment_count "$resource_group_scope" "$contributor_role_id"
)"

echo "Direct Grader Reader assignments: $subscription_direct_count"
echo "Direct Grader Contributor assignments: $resource_group_direct_count"

list_lighthouse_assignments() {
  local scope="$1"

  az rest \
    --method get \
    --url "$management_url$scope/providers/Microsoft.ManagedServices/registrationAssignments?api-version=$api_version" \
    --only-show-errors \
    --output json 2>/dev/null || printf '{"value":[]}\n'
}

subscription_lighthouse_json="$(list_lighthouse_assignments "$subscription_scope")"
resource_group_lighthouse_json="$(list_lighthouse_assignments "$resource_group_scope")"

lighthouse_authorization_count() {
  local assignments_json="$1"
  local role_id="$2"
  local count=0
  local definition_id definition_json matches

  while IFS= read -r definition_id; do
    [[ -n "$definition_id" ]] || continue
    definition_json="$(
      az rest \
        --method get \
        --url "$management_url$definition_id?api-version=$api_version" \
        --only-show-errors \
        --output json 2>/dev/null || printf '{}\n'
    )"
    matches="$(
      jq \
        --arg tenant "$grading_tenant_id" \
        --arg principal "$grading_principal_id" \
        --arg role "$role_id" \
        '[
          select((.properties.managedByTenantId // "" | ascii_downcase) == $tenant)
          | .properties.authorizations[]?
          | select(
              (.principalId // "" | ascii_downcase) == $principal
              and (.roleDefinitionId // "" | ascii_downcase) == $role
            )
        ] | length' \
        <<<"$definition_json"
    )"
    count=$((count + matches))
  done < <(
    jq -r \
      '.value[]? | select(.properties.provisioningState == "Succeeded") | .properties.registrationDefinitionId' \
      <<<"$assignments_json"
  )

  echo "$count"
}

subscription_lighthouse_reader_count="$(
  lighthouse_authorization_count "$subscription_lighthouse_json" "$reader_role_id"
)"
resource_group_lighthouse_reader_count="$(
  lighthouse_authorization_count "$resource_group_lighthouse_json" "$reader_role_id"
)"
resource_group_lighthouse_contributor_count="$(
  lighthouse_authorization_count "$resource_group_lighthouse_json" "$contributor_role_id"
)"

echo "Lighthouse subscription Reader authorizations: $subscription_lighthouse_reader_count"
echo "Lighthouse resource-group Reader authorizations: $resource_group_lighthouse_reader_count"
echo "Lighthouse resource-group Contributor authorizations: $resource_group_lighthouse_contributor_count"

if [[ "$expected_mode" == "direct" ]]; then
  if [[ "$subscription_direct_count" -gt 0 && "$resource_group_direct_count" -gt 0 ]]; then
    echo "PASS: required same-tenant direct RBAC assignments exist."
  else
    echo "FAIL: same-tenant direct RBAC assignments are incomplete."
    errors=$((errors + 1))
  fi

  if [[ "$subscription_lighthouse_reader_count" -gt 0 ||
        "$resource_group_lighthouse_reader_count" -gt 0 ||
        "$resource_group_lighthouse_contributor_count" -gt 0 ]]; then
    echo "FAIL: same-tenant subscription has unexpected Azure Lighthouse access."
    errors=$((errors + 1))
  else
    echo "PASS: no same-tenant Azure Lighthouse access was found."
  fi
else
  if [[ "$subscription_lighthouse_reader_count" -gt 0 &&
        "$resource_group_lighthouse_reader_count" -gt 0 &&
        "$resource_group_lighthouse_contributor_count" -gt 0 ]]; then
    echo "PASS: required cross-tenant Azure Lighthouse access exists."
  else
    echo "FAIL: cross-tenant Azure Lighthouse access is incomplete."
    errors=$((errors + 1))
  fi

  if [[ "$subscription_direct_count" -gt 0 || "$resource_group_direct_count" -gt 0 ]]; then
    echo "FAIL: cross-tenant subscription has unexpected direct grader RBAC."
    errors=$((errors + 1))
  else
    echo "PASS: no cross-tenant direct grader RBAC was found."
  fi
fi

if [[ "$errors" -ne 0 ]]; then
  echo "Verification failed with $errors problem(s). Re-run cloudshell-onboard.sh, then verify again." >&2
  exit 1
fi

echo "Verification passed. Azure Isekai grading access is configured correctly."
