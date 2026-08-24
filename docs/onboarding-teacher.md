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
creates a new grading principal ID. Update `grading_principal_id` in
`cloudshell-onboard.sh`, `cloudshell-offboard.sh`, and
`cloudshell-verify-access.sh` locally and in the Gist before asking students to
onboard again. Also update `grading_tenant_id` in all three files if the
deployment tenant changes.

Before destroying the old stack, ask every student to run the shared
`cloudshell-offboard.sh` launcher while it still contains the old principal ID.
The launcher automatically removes direct RBAC for same-tenant subscriptions
or Azure Lighthouse delegations for different-tenant subscriptions. Both modes
preserve `projProd`, remove the ownership tag, and can be rerun safely after a
partial cleanup.

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
a subscription ID or email address. Same-tenant students use
`cloudshell-onboard-direct.sh`; different-tenant students use
`cloudshell-onboard-lighthouse.sh`. Each launcher rejects an incompatible
tenant relationship before granting access. Both use the subscription and
signed-in identity currently selected in Cloud Shell and authorize only the
grading Function's managed identity by default. They do not grant the teacher
account access to student subscriptions, so those subscriptions and resource
groups do not clutter the teacher's Azure portal.

Before registration, ask the student to run the read-only verifier:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

It must report the expected mode and finish with `Verification passed`.
Same-tenant subscriptions require direct RBAC and reject grader Lighthouse
delegation; cross-tenant subscriptions require both Lighthouse delegations and
reject direct grader RBAC.
See [Verify student grading access](verify-student-grading-access.md) for the
required output, failure handling, and student-run end-to-end grading check.

The onboarding script will not replace a non-empty `GradingStudentEmail` tag
owned by another email. Teachers should investigate ownership disputes rather
than deleting or changing that tag automatically. Subscription owners can
still change Azure resources manually; the tag guard prevents accidental or
script-based reassignment, not actions by a hostile subscription administrator.

## 4. Register a Verified Subscription

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
If grading still fails, have the student rerun the verifier from the affected
subscription before granting teacher debug access.

Students can review their authenticated profile and reset current progress from
`pass-task.html`. A self-service reset preserves failed attempts, grading
reports, subscription registration, and Azure access.

## 6. Optional Teacher Debug Access

Only when interactive Azure portal or CLI debugging is required, ask the
student to run:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-debug-access.sh?v=$(date +%s)" \
  | bash -s -- grant
```

This adds the configured teacher principal as subscription `Reader` and
`projProd` `Website Contributor`. Remove that access immediately after
debugging while keeping grader access:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-debug-access.sh?v=$(date +%s)" \
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
