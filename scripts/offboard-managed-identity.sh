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

remove_lighthouse_resource() {
  local resource_type="$1"
  local resource_id="$2"
  local resource_group="$3"
  local label="$4"
  local -a scope_args=()

  if [[ -n "$resource_group" ]]; then
    scope_args=(--resource-group "$resource_group")
  fi

  if ! az managedservices "$resource_type" show \
    --subscription "$SUBSCRIPTION_ID" \
    "${scope_args[@]}" \
    "--$resource_type" "$resource_id" \
    --output none 2>/dev/null; then
    echo "$label does not exist."
    return
  fi

  az managedservices "$resource_type" delete \
    --subscription "$SUBSCRIPTION_ID" \
    "${scope_args[@]}" \
    "--$resource_type" "$resource_id" \
    --yes \
    --only-show-errors \
    --output none
  echo "Removed $label."
}

remove_lighthouse_access() {
  local subscription_definition_id subscription_assignment_id
  local resource_group_definition_id resource_group_assignment_id

  subscription_definition_id=$(lighthouse_definition_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "subscription" "reader")
  subscription_assignment_id=$(lighthouse_assignment_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "subscription" "reader")
  resource_group_definition_id=$(lighthouse_definition_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "resource-group" "$RESOURCE_GROUP")
  resource_group_assignment_id=$(lighthouse_assignment_id \
    "$SUBSCRIPTION_ID" "$GRADING_TENANT_ID" "$GRADING_PRINCIPAL_ID" \
    "resource-group" "$RESOURCE_GROUP")

  remove_lighthouse_resource assignment "$resource_group_assignment_id" "$RESOURCE_GROUP" \
    "resource-group assignment"
  remove_lighthouse_resource assignment "$subscription_assignment_id" "" \
    "subscription Reader assignment"
  remove_lighthouse_resource definition "$resource_group_definition_id" "" \
    "resource-group definition"
  remove_lighthouse_resource definition "$subscription_definition_id" "" \
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

az group update \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  --remove tags.GradingStudentEmail \
  --only-show-errors \
  >/dev/null

echo "Offboarding complete. Access mode: $ACCESS_MODE"
