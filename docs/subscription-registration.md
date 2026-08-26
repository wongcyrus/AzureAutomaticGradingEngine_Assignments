# Subscription Registration Workflow

Subscription onboarding and Azure Isekai registration are separate:

- **Onboarding** grants the grading identity access to Azure and sets an
  initialization tag on `projProd`.
- **Registration** binds one authenticated Azure Isekai student to one Azure
  subscription.

Students register only through the Azure Isekai web page. There is no batch
import path.

## Student Workflow

1. Sign in to Azure Cloud Shell with the same email used for Azure Isekai.
2. Select the assignment subscription.
3. Run the correct onboarding launcher:
   - [Same tenant](onboarding-student-same-tenant.md)
   - [Different tenant](onboarding-student-cross-tenant.md)
4. Run the [read-only access verifier](verify-student-grading-access.md).
5. Sign in to Azure Isekai with the same email.
6. Open the registration page and submit the subscription ID printed by the
   onboarding launcher.
7. Start grading only after registration reports success.

The registration page ignores any submitted email value. The backend uses only
the identity signed by the authenticated Static Web Apps proxy.

## Initial Validation

For the first registration, the backend:

1. Validates the subscription ID as a GUID.
2. Confirms the grading identity can access `projProd`.
3. Confirms `GradingStudentEmail` exactly matches the authenticated student.
4. Atomically reserves both the normalized student email and normalized
   subscription ID.

The onboarding tag is proof of control only during this initial claim. After a
successful registration, the database is authoritative. Changing or deleting
the Azure tag does not transfer the registration and does not allow another
student to claim the subscription.

## One-to-One Registration

`SubscriptionRegistrations` stores two indexes in one Azure Table transaction:

- A hashed email index resolves the authenticated student to a subscription.
- A subscription index prevents that subscription from being claimed by a
  different student.

Both indexes are created or deleted together. Registration outcomes are:

| Situation | Result |
| --- | --- |
| Neither email nor subscription is registered | Registration succeeds. |
| The same email/subscription pair already exists | Idempotent success. |
| The email is bound to another subscription | Conflict; contact the teacher. |
| The subscription is bound to another email | Conflict; contact the teacher. |
| Only one index exists or the pair disagrees | Integrity error; contact the teacher. |
| The tag or grader access is missing | Rerun onboarding and verification. |

Conflict responses never disclose another student's email or subscription.

## Progress Reset Versus Registration Release

A game-progress reset does not release the subscription. It preserves the
registration, Azure access, failed attempts, and reports.

Only an administrator can release a registration:

```bash
scripts/release-student-subscription.sh <student-email>
```

The command verifies both indexes, displays the pair, requests confirmation,
and deletes both atomically. It does not change Azure access, tags, progress,
reports, or test results.

## Reassignment Workflow

To transfer a subscription safely:

1. Ask the current student to run the offboarding launcher. This removes grader
   access and the initialization tag while preserving `projProd`.
2. Run `scripts/release-student-subscription.sh` for the current student.
3. Ask the new student to run onboarding from that subscription.
4. Ask the new student to run the verifier.
5. The new student registers through Azure Isekai.

Use the same sequence when one student must replace their registered
subscription. Do not change only the Azure tag; the existing database claim
will remain in force.

## Clean Registration Reset

The registration redesign intentionally has no legacy migration. Deploying the
new storage schema replaces the old registration table with
`SubscriptionRegistrations`. Existing students must register again through the
web page after deployment and successful onboarding verification.
