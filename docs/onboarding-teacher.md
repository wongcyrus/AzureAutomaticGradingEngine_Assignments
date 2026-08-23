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
is configured for the current deployed identity. A clean destroy and redeploy
creates a new grading principal ID. Update `grading_principal_id` in both
`cloudshell-onboard.sh` and `cloudshell-offboard.sh` locally and in the Gist
before asking students to onboard again. Also update `grading_tenant_id` if the
deployment tenant changes.

Before destroying the old stack, ask every student to run the shared
`cloudshell-offboard.sh` launcher while it still contains the old principal ID.
The launcher automatically removes direct RBAC for same-tenant subscriptions
or Azure Lighthouse delegations for different-tenant subscriptions.

## 2. Grant Azure Isekai Sign-In Access

Add each student's Azure Isekai email to `Infrastructure/students.txt`, one per
line, then run:

```bash
cd Infrastructure
npm run students:invite -- students.txt
```

The command is idempotent. It adds existing same-tenant users to the
`GradingEngineAssignmentStudents` group. For different-tenant users, it first
sends a Microsoft Entra guest invitation and then adds the guest to the group.
External students must accept that invitation before signing in to Azure
Isekai. This group grants game sign-in only; it does not grant access to student
Azure subscriptions.

## 3. Give Students the Correct Guide

- [Student in the same tenant](onboarding-student-same-tenant.md)
- [Student in a different tenant](onboarding-student-cross-tenant.md)

Both students use Azure Cloud Shell without cloning this repository or entering
a subscription ID or email address. The launcher uses the subscription and
signed-in identity currently selected in Cloud Shell. By default it authorizes
only the grading Function's managed identity. It does not grant the teacher
account access to student subscriptions, so those subscriptions and resource
groups do not clutter the teacher's Azure portal.

## 4. Register an Onboarded Subscription

Students can register the subscription ID displayed by the launcher in Azure
Isekai. A teacher can instead import it:

```bash
cd Infrastructure
npm run students:import -- <student-email> <subscription-id>
```

The import verifies the `GradingStudentEmail` ownership tag and either direct
grader RBAC or both deterministic Lighthouse delegations.

## 5. Test Grading

```bash
scripts/test-deployed-function.sh \
  GradingEngineAssignmentResourceGroup \
  azureisekai2026 \
  <student-subscription-id> \
  <student-email>
```

Azure role and Lighthouse changes can take several minutes to propagate.

Students can review their authenticated profile and reset current progress from
`pass-task.html`. A self-service reset preserves failed attempts, grading
reports, subscription registration, and Azure access.

## 6. Optional Teacher Debug Access

Only when interactive Azure portal or CLI debugging is required, ask the
student to run:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-debug-access.sh \
  | bash -s -- grant
```

This adds the configured teacher principal as subscription `Reader` and
`projProd` `Website Contributor`. Remove that access immediately after
debugging while keeping grader access:

```bash
curl -fsSL https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-debug-access.sh \
  | bash -s -- revoke
```

## 7. Revoke Grader Access

```bash
scripts/offboard-managed-identity.sh \
  -s <student-subscription-id> \
  -p <grading-identity-principal-id> \
  -t <grading-identity-tenant-id> \
  -e <student-email> \
  -i <instructor-user-object-id>
```

Omit `-i` for normal onboarding. Supply it only when removing a delegation that
still contains a teacher debug authorization.
