#!/usr/bin/env bash

set -euo pipefail

SUBSCRIPTION_ID=""
GRADING_PRINCIPAL_ID=""
GRADING_TENANT_ID=""
STUDENT_EMAIL=""
RESOURCE_GROUP="projProd"

usage() {
  cat <<'EOF'
Usage:
  onboard-managed-identity.sh \
    -s <student-subscription-id> \
    -p <grading-managed-identity-principal-id> \
    -t <grading-tenant-id> \
    -e <azure-isekai-sign-in-email> \
    [-g <assignment-resource-group>]

Grants:
  - Reader on the student subscription
  - Website Contributor on the assignment resource group
EOF
}

while getopts ":s:p:t:e:g:h" opt; do
  case "$opt" in
    s) SUBSCRIPTION_ID="$OPTARG" ;;
    p) GRADING_PRINCIPAL_ID="$OPTARG" ;;
    t) GRADING_TENANT_ID="$OPTARG" ;;
    e) STUDENT_EMAIL="${OPTARG,,}" ;;
    g) RESOURCE_GROUP="$OPTARG" ;;
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

az account show --subscription "$SUBSCRIPTION_ID" >/dev/null

STUDENT_TENANT_ID=$(az account show \
  --subscription "$SUBSCRIPTION_ID" \
  --query tenantId \
  -o tsv)

if [[ "${STUDENT_TENANT_ID,,}" != "${GRADING_TENANT_ID,,}" ]]; then
  echo "This subscription is in tenant '$STUDENT_TENANT_ID', but the grader is in '$GRADING_TENANT_ID'." >&2
  echo "Direct managed-identity RBAC requires both to use the same tenant." >&2
  exit 4
fi

if ! az group show \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  >/dev/null 2>&1; then
  echo "Resource group '$RESOURCE_GROUP' does not exist in subscription '$SUBSCRIPTION_ID'." >&2
  echo "Create the assignment resources before running this script." >&2
  exit 5
fi

SUBSCRIPTION_SCOPE="/subscriptions/$SUBSCRIPTION_ID"
RESOURCE_GROUP_SCOPE="$SUBSCRIPTION_SCOPE/resourceGroups/$RESOURCE_GROUP"
READER_ROLE_ID="acdd72a7-3385-48ef-bd42-f606fba81ae7"
WEBSITE_CONTRIBUTOR_ROLE_ID="de139f84-1756-47ae-9be6-808fbbe84772"

ensure_role_assignment() {
  local role_id="$1"
  local scope="$2"
  local label="$3"
  local assignment_count

  assignment_count=$(az role assignment list \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$GRADING_PRINCIPAL_ID" \
    --scope "$scope" \
    --query "[?ends_with(roleDefinitionId, '$role_id')] | length(@)" \
    -o tsv)

  if [[ "$assignment_count" != "0" ]]; then
    echo "$label is already assigned at $scope."
    return
  fi

  az role assignment create \
    --subscription "$SUBSCRIPTION_ID" \
    --assignee-object-id "$GRADING_PRINCIPAL_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "$role_id" \
    --scope "$scope" \
    --only-show-errors \
    >/dev/null
  echo "Assigned $label at $scope."
}

ensure_role_assignment "$READER_ROLE_ID" "$SUBSCRIPTION_SCOPE" "Reader"
ensure_role_assignment "$WEBSITE_CONTRIBUTOR_ROLE_ID" "$RESOURCE_GROUP_SCOPE" "Website Contributor"

az group update \
  --subscription "$SUBSCRIPTION_ID" \
  --name "$RESOURCE_GROUP" \
  --set "tags.GradingStudentEmail=$STUDENT_EMAIL" \
  --only-show-errors \
  >/dev/null
echo "Tagged $RESOURCE_GROUP for $STUDENT_EMAIL."

cat <<EOF

Onboarding complete.
Register this subscription ID in Azure Isekai:
$SUBSCRIPTION_ID

RBAC changes can take several minutes to propagate.
EOF
