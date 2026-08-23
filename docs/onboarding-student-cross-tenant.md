# Student Onboarding: Different Tenant

Use this guide when the assignment subscription belongs to a different
Microsoft Entra tenant from the Azure Isekai grader.

## Requirements

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
plus `Website Contributor` on `projProd`. To use another Azure location, pass
it as the second argument:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard.sh \
  | bash -s -- "<azure-isekai-sign-in-email>" "<location>"
```

After Lighthouse permissions propagate, sign in to Azure Isekai with the same
email and register the subscription ID printed by the launcher.
