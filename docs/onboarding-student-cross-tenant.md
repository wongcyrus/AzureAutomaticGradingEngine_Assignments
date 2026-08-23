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
- Use the same email address that you use to sign in to Azure Isekai.

Confirm the currently selected subscription:

```bash
az account show --query '{name:name,id:id,tenant:tenantId}' -o table
```

## Onboard

No repository clone or subscription argument is required:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard.sh \
  | bash -s -- "<azure-isekai-sign-in-email>"
```

The launcher reports `cross-tenant Azure Lighthouse`, creates `projProd` in
`eastasia` if needed, delegates subscription `Reader`, and delegates `Reader`
plus `Website Contributor` on `projProd` to the grading Function identity only.
It does not project the subscription into the teacher's Azure portal. To use
another Azure location, pass it as the second argument:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard.sh \
  | bash -s -- "<azure-isekai-sign-in-email>" "<location>"
```

After Lighthouse permissions propagate, sign in to Azure Isekai with the same
email and register the subscription ID printed by the launcher.

## Remove Azure Isekai Access

Before the teacher destroys and redeploys the grading stack, select the
assignment subscription in Cloud Shell and run:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-offboard.sh \
  | bash -s -- "<azure-isekai-sign-in-email>"
```

This removes the Azure Lighthouse subscription and resource-group delegations,
including any temporary teacher debug authorization, and clears the Azure
Isekai ownership tag. It keeps the `projProd` resource group and its assignment
resources. The same launcher is used for both same-tenant and different-tenant
subscriptions; it detects the access mode automatically.
