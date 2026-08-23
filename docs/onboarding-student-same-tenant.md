# Student Onboarding: Same Tenant

Use this guide when the assignment subscription and Azure Isekai grader belong
to the same Microsoft Entra tenant.

## Requirements

- The teacher has added your email to the Azure Isekai student group.
- Open Azure Cloud Shell with the assignment subscription selected.
- Be an Owner or User Access Administrator of that subscription.
- Sign in to Cloud Shell with the same email address used for Azure Isekai.

Confirm the currently selected subscription:

```bash
az account show --query '{name:name,id:id,tenant:tenantId}' -o table
```

## Onboard

No repository clone, subscription ID, or email argument is required. The
launcher reads the active subscription and signed-in email from Azure CLI:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard.sh?v=$(date +%s)" \
  | bash
```

The launcher reports `same-tenant direct RBAC`, creates `projProd` in
`eastasia` if needed, grants the grader `Reader` on the subscription and
`Website Contributor` on `projProd`, and tags the resource group with your
email. It does not grant your teacher access to the subscription. To use
another Azure location, pass it as the second argument:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard.sh?v=$(date +%s)" \
  | bash -s -- "<location>"
```

After permissions propagate, sign in to Azure Isekai with the same email and
register the subscription ID printed by the launcher.

## Remove Azure Isekai Access

Before the teacher destroys and redeploys the grading stack, select the
assignment subscription in Cloud Shell and run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-offboard.sh?v=$(date +%s)" \
  | bash
```

This removes the grader's direct RBAC assignments, removes any temporary
teacher debug assignments, and clears the Azure Isekai ownership tag. It keeps
the `projProd` resource group and its assignment resources. The same launcher
is used for both same-tenant and different-tenant subscriptions; it detects the
access mode automatically.

Successful same-tenant cleanup ends with `Offboarding complete. Access mode:
direct`. The launcher is safe to rerun after a partial cleanup; already removed
assignments are reported as missing and skipped.
