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
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard-direct.sh?v=$(date +%s)" \
  | bash
```

This launcher requires the selected subscription and grader to be in the same
tenant; it exits before granting access when they differ. It reports
`same-tenant direct RBAC`, creates `projProd` in `eastasia` if needed, grants
the grader `Reader` on the subscription and `Website Contributor` on
`projProd`, and tags the resource group with your email. It does not grant your
teacher access to the subscription. To use another Azure location, pass it as
the second argument:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-onboard-direct.sh?v=$(date +%s)" \
  | bash -s -- "<location>"
```

The first successful onboarding claims `projProd` with the
`GradingStudentEmail` tag. Repeated runs by the same Cloud Shell email are
safe. A different email is rejected before any grader role or tag is changed.

If the launcher cannot be used, follow the
[manual same-tenant setup and verification guide](manual-onboarding-direct.md).

After permissions propagate, sign in to Azure Isekai with the same email and
register the subscription ID printed by the launcher.

## Verify Access

Follow the complete
[student grading-access verification guide](verify-student-grading-access.md).
From the same Cloud Shell subscription, run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

It prints the detected tenant IDs, expected mode, ownership tag, direct role
counts, and Lighthouse authorization counts. Same-tenant verification succeeds
only when direct `Reader` and `Website Contributor` assignments exist and no
grader Lighthouse delegation exists.

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
