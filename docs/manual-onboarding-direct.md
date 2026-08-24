# Manual Same-Tenant Setup and Verification

Use these commands in Azure Cloud Shell when the assignment subscription and
Azure Isekai grader are in the same Microsoft Entra tenant. This is the manual
alternative to `cloudshell-onboard-direct.sh`.

Requirements: Azure CLI and `jq` in Cloud Shell, plus Owner or User Access
Administrator on the selected subscription.

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

if [[ "${student_tenant_id,,}" != "$grading_tenant_id" ]]; then
  echo "Stop: use the cross-tenant Lighthouse guide." >&2
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
  --location eastasia \
  --only-show-errors \
  --output none

registered_email="$(
  az group show \
    --subscription "$subscription_id" \
    --name projProd \
    --query "tags.GradingStudentEmail" \
    -o tsv
)"

if [[ -n "$registered_email" &&
      "${registered_email,,}" != "$student_email" ]]; then
  echo "Stop: projProd is already registered to $registered_email." >&2
  onboarding_safe=false
fi

echo "Safe to continue: $onboarding_safe"
```

Never overwrite another email's ownership tag. Ask the teacher to investigate
an ownership mismatch. Do not run the role-assignment section unless
`onboarding_safe` remains `true`.

## 3. Grant Direct Grader Roles

```bash
subscription_scope="/subscriptions/$subscription_id"
resource_group_scope="$subscription_scope/resourceGroups/projProd"

if [[ "$(
  az role assignment list \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --scope "$subscription_scope" \
    --query "[?ends_with(roleDefinitionId, '/$reader_role_id') && scope == '$subscription_scope'] | length(@)" \
    -o tsv
)" == "0" ]]; then
  az role assignment create \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --assignee-principal-type ServicePrincipal \
    --role "$reader_role_id" \
    --scope "$subscription_scope" \
    --only-show-errors \
    --output none
fi

if [[ "$(
  az role assignment list \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --scope "$resource_group_scope" \
    --query "[?ends_with(roleDefinitionId, '/$website_contributor_role_id') && scope == '$resource_group_scope'] | length(@)" \
    -o tsv
)" == "0" ]]; then
  az role assignment create \
    --subscription "$subscription_id" \
    --assignee-object-id "$grading_principal_id" \
    --assignee-principal-type ServicePrincipal \
    --role "$website_contributor_role_id" \
    --scope "$resource_group_scope" \
    --only-show-errors \
    --output none
fi

az group update \
  --subscription "$subscription_id" \
  --name projProd \
  --set "tags.GradingStudentEmail=$student_email" \
  --only-show-errors \
  --output none
```

These commands are safe to repeat. Existing role assignments are skipped.

## 4. Verify

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

The verifier must report `Expected access mode: direct`, both direct assignment
counts above zero, and all Lighthouse authorization counts as zero.

To remove access while preserving `projProd`, run `cloudshell-offboard.sh` from
the onboarding Gist.
