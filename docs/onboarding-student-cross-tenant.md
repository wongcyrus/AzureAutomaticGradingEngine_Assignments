# Student Onboarding: Different Tenant

Use this guide when the assignment subscription belongs to a different
Microsoft Entra tenant from the Azure Isekai grader.

## Requirements

- The teacher has invited your email to Azure Isekai and added it to the
  student group.
- Accept the Microsoft Entra guest invitation before signing in to Azure
  Isekai.
- Open Azure Cloud Shell with the assignment subscription selected.
- Be an Owner of that subscription, or have a custom role with
  `Microsoft.Authorization/roleAssignments` read, write, and delete
  permissions.
- Sign in to Cloud Shell with the same email address used for Azure Isekai.

Confirm the currently selected subscription:

```bash
az account show --query '{name:name,id:id,tenant:tenantId}' -o table
```

## Onboard

No repository clone, subscription ID, or email argument is required. The
launcher reads the active subscription and signed-in email from Azure CLI:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard-lighthouse.sh?v=$(date +%s)" \
  | bash
```

This launcher requires the selected subscription and grader to be in different
tenants; it exits before creating a delegation when they match. It reports
`cross-tenant Azure Lighthouse`, creates `projProd` in `brazilsouth` if needed,
delegates subscription `Reader`, and delegates `Reader` plus `Contributor` on
`projProd` to the grading Function identity only. It does not
project the subscription into the teacher's Azure portal. Brazil South is
fixed by the grading suite and cannot be overridden.

The first successful onboarding claims `projProd` with the
`GradingStudentEmail` tag. Repeated runs by the same Cloud Shell email are
safe. A different email is rejected before any Lighthouse deployment or tag is
changed.

If the launcher cannot be used, follow the
[manual cross-tenant setup and verification guide](manual-onboarding-lighthouse.md).

After Lighthouse permissions propagate, sign in to Azure Isekai with the same
email and register the subscription ID printed by the launcher.

## Verify Access

Follow the complete
[student grading-access verification guide](verify-student-grading-access.md).
From the same Cloud Shell subscription, run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

It prints the detected tenant IDs, expected mode, ownership tag, direct role
counts, and Lighthouse authorization counts. Cross-tenant verification
succeeds only when both required Lighthouse delegations exist and no direct
grader assignments exist.

## Remove Azure Isekai Access

Before the teacher destroys and redeploys the grading stack, select the
assignment subscription in Cloud Shell and run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-offboard.sh?v=$(date +%s)" \
  | bash
```

This removes the Azure Lighthouse subscription and resource-group delegations,
including any temporary teacher debug authorization, and clears the Azure
Isekai ownership tag. It keeps the `projProd` resource group and its assignment
resources. The same launcher is used for both same-tenant and different-tenant
subscriptions; it detects the access mode automatically.

Successful cross-tenant cleanup ends with `Offboarding complete. Access mode:
lighthouse`. The launcher discovers every assignment that references the Azure
Lighthouse definitions and is safe to rerun after a partial cleanup.
