# Manual Cross-Tenant Setup and Verification

Use these commands in Azure Cloud Shell when the assignment subscription and
Azure Isekai grader are in different Microsoft Entra tenants. This is the
manual alternative to `cloudshell-onboard-lighthouse.sh`.

Requirements: Azure CLI, `jq`, and `sha256sum` in Cloud Shell, plus Owner or
equivalent Managed Services deployment permissions on the selected
subscription.

## 1. Select and Validate the Subscription

```bash
grading_principal_id="078c7abf-66ed-409c-9e40-e8fdb6a93221"
grading_tenant_id="8ff7db19-435d-4c3c-83d3-ca0a46234f51"
reader_role_id="acdd72a7-3385-48ef-bd42-f606fba81ae7"
website_contributor_role_id="de139f84-1756-47ae-9be6-808fbbe84772"

subscription_id="$(az account show --query id -o tsv)"
student_tenant_id="$(az account show --query tenantId -o tsv)"
student_email="$(az account show --query user.name -o tsv)"
student_email="${student_email,,}"
onboarding_safe=true

echo "Subscription: $subscription_id"
echo "Student tenant: $student_tenant_id"
echo "Grading tenant: $grading_tenant_id"
echo "Student email: $student_email"

if [[ "${student_tenant_id,,}" == "$grading_tenant_id" ]]; then
  echo "Stop: use the same-tenant direct RBAC guide." >&2
  onboarding_safe=false
fi

echo "Safe to continue: $onboarding_safe"
```

Do not continue unless the displayed subscription and email are correct and
`Safe to continue` is `true`. This check deliberately does not call `exit`,
which would close an interactive Cloud Shell session.

## 2. Create or Validate `projProd`

```bash
az group create \
  --subscription "$subscription_id" \
  --name projProd \
  --location brazilsouth \
  --only-show-errors \
  --output none

registered_email="$(
  az group show \
    --subscription "$subscription_id" \
    --name projProd \
    --query "tags.GradingStudentEmail" \
    -o tsv
)"
resource_group_location="$(
  az group show \
    --subscription "$subscription_id" \
    --name projProd \
    --query location \
    -o tsv
)"

if [[ "${resource_group_location,,}" != "brazilsouth" ]]; then
  echo "Stop: projProd is in $resource_group_location; Brazil South is required." >&2
  onboarding_safe=false
fi

if [[ -n "$registered_email" &&
      "${registered_email,,}" != "$student_email" ]]; then
  echo "Stop: projProd is already registered to $registered_email." >&2
  onboarding_safe=false
fi

echo "Safe to continue: $onboarding_safe"
```

Never overwrite another email's ownership tag. Ask the teacher to investigate
an ownership mismatch. Do not run the Lighthouse deployment sections unless
`onboarding_safe` remains `true`.

## 3. Download the Lighthouse Templates

```bash
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
cache_buster="$(date +%s%N)"
work_dir="$(mktemp -d)"
trap 'rm -f "$work_dir/subscription.json" "$work_dir/resource-group.json"; rmdir "$work_dir"' EXIT

curl -fsSLo "$work_dir/subscription.json" \
  "$gist_base/subscription.json?v=$cache_buster"
curl -fsSLo "$work_dir/resource-group.json" \
  "$gist_base/resource-group.json?v=$cache_buster"
```

## 4. Create Deterministic Lighthouse Delegations

```bash
deterministic_guid() {
  local digest
  digest="$(printf '%s' "$1" | sha256sum | cut -c1-32)"
  printf '%s-%s-%s-%s-%s\n' \
    "${digest:0:8}" "${digest:8:4}" "${digest:12:4}" \
    "${digest:16:4}" "${digest:20:12}"
}

subscription_definition_id="$(
  deterministic_guid "azure-isekai|definition|$subscription_id|$grading_tenant_id|$grading_principal_id|subscription|reader"
)"
subscription_assignment_id="$(
  deterministic_guid "azure-isekai|assignment|$subscription_id|$grading_tenant_id|$grading_principal_id|subscription|reader"
)"
resource_group_definition_id="$(
  deterministic_guid "azure-isekai|definition|$subscription_id|$grading_tenant_id|$grading_principal_id|resource-group|projProd"
)"
resource_group_assignment_id="$(
  deterministic_guid "azure-isekai|assignment|$subscription_id|$grading_tenant_id|$grading_principal_id|resource-group|projProd"
)"

subscription_authorizations="$(
  jq -nc \
    --arg principal "$grading_principal_id" \
    --arg role "$reader_role_id" \
    '[{
      principalId: $principal,
      principalIdDisplayName: "Azure Isekai grader",
      roleDefinitionId: $role
    }]'
)"
resource_group_authorizations="$(
  jq -nc \
    --arg principal "$grading_principal_id" \
    --arg reader "$reader_role_id" \
    --arg website "$website_contributor_role_id" \
    '[
      {
        principalId: $principal,
        principalIdDisplayName: "Azure Isekai grader",
        roleDefinitionId: $reader
      },
      {
        principalId: $principal,
        principalIdDisplayName: "Azure Isekai grader",
        roleDefinitionId: $website
      }
    ]'
)"

az deployment sub create \
  --subscription "$subscription_id" \
  --location eastus \
  --name "azure-isekai-reader-$subscription_assignment_id" \
  --template-file "$work_dir/subscription.json" \
  --parameters \
    registrationDefinitionId="$subscription_definition_id" \
    registrationAssignmentId="$subscription_assignment_id" \
    offerName="Azure Isekai subscription Reader" \
    managedByTenantId="$grading_tenant_id" \
    authorizations="$subscription_authorizations" \
  --only-show-errors \
  --output none

az deployment sub create \
  --subscription "$subscription_id" \
  --location eastus \
  --name "azure-isekai-resources-$resource_group_assignment_id" \
  --template-file "$work_dir/resource-group.json" \
  --parameters \
    registrationDefinitionId="$resource_group_definition_id" \
    registrationAssignmentId="$resource_group_assignment_id" \
    offerName="Azure Isekai assignment resources" \
    managedByTenantId="$grading_tenant_id" \
    authorizations="$resource_group_authorizations" \
    resourceGroupName=projProd \
  --only-show-errors \
  --output none

az group update \
  --subscription "$subscription_id" \
  --name projProd \
  --set "tags.GradingStudentEmail=$student_email" \
  --only-show-errors \
  --output none
```

Deterministic IDs make these deployments safe to repeat.

## 5. Verify

Follow the complete
[student grading-access verification guide](verify-student-grading-access.md).
Run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

Success ends with:

```text
Verification passed. Azure Isekai grading access is configured correctly.
```

The verifier must report `Expected access mode: lighthouse`, all three
Lighthouse authorization counts above zero, and both direct assignment counts
as zero.

To remove access while preserving `projProd`, run `cloudshell-offboard.sh` from
the onboarding Gist.
