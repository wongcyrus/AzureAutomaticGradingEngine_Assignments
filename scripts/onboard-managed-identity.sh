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
  onboard-managed-identity.sh \
    -s <student-subscription-id> \
    -p <grading-managed-identity-principal-id> \
    -t <grading-tenant-id> \
    -e <azure-isekai-sign-in-email> \
    [-i <instructor-user-object-id>] \
    [-g <assignment-resource-group>] \
    [-m <auto|direct|lighthouse>]

Grants:
  Same tenant: direct Azure RBAC.
  Cross tenant: Azure Lighthouse delegation.

  Both modes grant:
    - Reader on the student subscription
    - Website Contributor on the assignment resource group
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

if [[ -z "$SUBSCRIPTION_ID" || -z "$GRADING_PRINCIPAL_ID" || -z "$GRADING_TENANT_ID" || -z "$STUDENT_EMAIL" ]]; then
  usage
  exit 2
fi

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI is required." >&2
  exit 3
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

if ! az group show \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  >/dev/null 2>&1; then
  echo "Resource group '$RESOURCE_GROUP' does not exist in subscription '$SUBSCRIPTION_ID'." >&2
  echo "Create the assignment resources before running this script." >&2
  exit 5
fi

REGISTERED_EMAIL=$(az group show \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  --query "tags.GradingStudentEmail" \
  -o tsv)
if [[ -n "$REGISTERED_EMAIL" && "${REGISTERED_EMAIL,,}" != "$STUDENT_EMAIL" ]]; then
  echo "The resource group is already registered to '$REGISTERED_EMAIL', not '$STUDENT_EMAIL'." >&2
  echo "No grader access or ownership tag was changed." >&2
  exit 6
fi

SUBSCRIPTION_SCOPE="/subscriptions/$SUBSCRIPTION_ID"
RESOURCE_GROUP_SCOPE="$SUBSCRIPTION_SCOPE/resourceGroups/$RESOURCE_GROUP"

ensure_role_assignment() {
  local principal_id="$1"
  local principal_type="$2"
  local role_id="$3"
  local scope="$4"
  local label="$5"
  local assignment_count

  assignment_count=$(az role assignment list \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$principal_id" \
    --scope "$scope" \
    --query "[?ends_with(roleDefinitionId, '$role_id')] | length(@)" \
    -o tsv)

  if [[ "$assignment_count" != "0" ]]; then
    echo "$label is already assigned at $scope."
    return
  fi

  az role assignment create \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$principal_id" \
    --assignee-principal-type "$principal_type" \
    --role "$role_id" \
    --scope "$scope" \
    --only-show-errors \
    >/dev/null
  echo "Assigned $label at $scope."
}

build_lighthouse_authorizations() {
  local include_website_contributor="$1"
  local authorizations

  authorizations="[{\"principalId\":\"$GRADING_PRINCIPAL_ID\",\"principalIdDisplayName\":\"Azure Isekai grader\",\"roleDefinitionId\":\"$READER_ROLE_ID\"}"
  if [[ "$include_website_contributor" == "true" ]]; then
    authorizations+=",{\"principalId\":\"$GRADING_PRINCIPAL_ID\",\"principalIdDisplayName\":\"Azure Isekai grader\",\"roleDefinitionId\":\"$WEBSITE_CONTRIBUTOR_ROLE_ID\"}"
  fi
  if [[ -n "$INSTRUCTOR_PRINCIPAL_ID" ]]; then
    authorizations+=",{\"principalId\":\"$INSTRUCTOR_PRINCIPAL_ID\",\"principalIdDisplayName\":\"Azure Isekai instructor\",\"roleDefinitionId\":\"$READER_ROLE_ID\"}"
    if [[ "$include_website_contributor" == "true" ]]; then
      authorizations+=",{\"principalId\":\"$INSTRUCTOR_PRINCIPAL_ID\",\"principalIdDisplayName\":\"Azure Isekai instructor\",\"roleDefinitionId\":\"$WEBSITE_CONTRIBUTOR_ROLE_ID\"}"
    fi
  fi
  authorizations+="]"
  echo "$authorizations"
}

deploy_lighthouse_access() {
  local subscription_definition_id subscription_assignment_id
  local resource_group_definition_id resource_group_assignment_id
  local subscription_authorizations resource_group_authorizations
  local deployment_location="${AZURE_LIGHTHOUSE_DEPLOYMENT_LOCATION:-eastus}"

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
  subscription_authorizations=$(build_lighthouse_authorizations false)
  resource_group_authorizations=$(build_lighthouse_authorizations true)

  az deployment sub create \
    --subscription "$SUBSCRIPTION_ID" \
    --location "$deployment_location" \
    --name "azure-isekai-reader-$subscription_assignment_id" \
    --template-file "$SCRIPT_DIR/lighthouse/subscription.json" \
    --parameters \
      registrationDefinitionId="$subscription_definition_id" \
      registrationAssignmentId="$subscription_assignment_id" \
      offerName="Azure Isekai subscription Reader" \
      managedByTenantId="$GRADING_TENANT_ID" \
      authorizations="$subscription_authorizations" \
    --only-show-errors \
    --output none

  az deployment sub create \
    --subscription "$SUBSCRIPTION_ID" \
    --location "$deployment_location" \
    --name "azure-isekai-resources-$resource_group_assignment_id" \
    --template-file "$SCRIPT_DIR/lighthouse/resource-group.json" \
    --parameters \
      registrationDefinitionId="$resource_group_definition_id" \
      registrationAssignmentId="$resource_group_assignment_id" \
      offerName="Azure Isekai assignment resources" \
      managedByTenantId="$GRADING_TENANT_ID" \
      authorizations="$resource_group_authorizations" \
      resourceGroupName="$RESOURCE_GROUP" \
    --only-show-errors \
    --output none

  echo "Created or verified Azure Lighthouse subscription and resource-group delegations."
}

if [[ "$ACCESS_MODE" == "direct" ]]; then
  ensure_role_assignment \
    "$GRADING_PRINCIPAL_ID" ServicePrincipal \
    "$READER_ROLE_ID" "$SUBSCRIPTION_SCOPE" "Grader Reader"
  ensure_role_assignment \
    "$GRADING_PRINCIPAL_ID" ServicePrincipal \
    "$WEBSITE_CONTRIBUTOR_ROLE_ID" "$RESOURCE_GROUP_SCOPE" "Grader Website Contributor"

  if [[ -n "$INSTRUCTOR_PRINCIPAL_ID" ]]; then
    ensure_role_assignment \
      "$INSTRUCTOR_PRINCIPAL_ID" User \
      "$READER_ROLE_ID" "$SUBSCRIPTION_SCOPE" "Instructor Reader"
    ensure_role_assignment \
      "$INSTRUCTOR_PRINCIPAL_ID" User \
      "$WEBSITE_CONTRIBUTOR_ROLE_ID" "$RESOURCE_GROUP_SCOPE" "Instructor Website Contributor"
  fi
else
  deploy_lighthouse_access
fi

az group update \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  --set "tags.GradingStudentEmail=$STUDENT_EMAIL" \
  --only-show-errors \
  >/dev/null
echo "Tagged $RESOURCE_GROUP for $STUDENT_EMAIL."

cat <<EOF

Onboarding complete.
Access mode: $ACCESS_MODE
Register this subscription ID in Azure Isekai:
$SUBSCRIPTION_ID

Azure authorization changes can take several minutes to propagate.
EOF
