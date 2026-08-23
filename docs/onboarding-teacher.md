# Teacher Onboarding Guide

Use this guide after deploying the grading engine.

## 1. Confirm the Grading Identity

```bash
cd Infrastructure
npx cdktn output AzureAutomaticGradingEngineGrader
```

Record `grading_identity_principal_id` and `grading_identity_tenant_id`. The
maintained unlisted
[Cloud Shell onboarding Gist](https://gist.github.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c)
is configured for the current deployed identity. Update the Gist if either
identity value changes.

## 2. Give Students the Correct Guide

- [Student in the same tenant](onboarding-student-same-tenant.md)
- [Student in a different tenant](onboarding-student-cross-tenant.md)

Both students use Azure Cloud Shell without cloning this repository or entering
a subscription ID. The launcher uses the subscription currently selected in
Cloud Shell.

## 3. Register an Onboarded Subscription

Students can register the subscription ID displayed by the launcher in Azure
Isekai. A teacher can instead import it:

```bash
cd Infrastructure
npm run students:import -- <student-email> <subscription-id>
```

The import verifies the `GradingStudentEmail` ownership tag and either direct
grader RBAC or both deterministic Lighthouse delegations.

## 4. Test Grading

```bash
scripts/test-deployed-function.sh \
  GradingEngineAssignmentResourceGroup \
  azureisekai2026 \
  <student-subscription-id> \
  <student-email>
```

Azure role and Lighthouse changes can take several minutes to propagate.

## 5. Revoke Access

```bash
scripts/offboard-managed-identity.sh \
  -s <student-subscription-id> \
  -p <grading-identity-principal-id> \
  -t <grading-identity-tenant-id> \
  -e <student-email> \
  -i <instructor-user-object-id>
```

The instructor ID is optional. For same-tenant onboarding, include the same ID
used when granting instructor access.
