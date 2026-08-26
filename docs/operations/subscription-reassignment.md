# Subscription Reassignment Runbook

Use this sequence when a subscription moves to another student or one student
must replace their registered subscription.

1. Ask the current student to run the maintained offboarding launcher while it
   still references the current grading principal.
2. Confirm grader RBAC or Lighthouse delegation and `GradingStudentEmail` were
   removed.
3. Release the current registration:

   ```bash
   scripts/release-student-subscription.sh student@example.com
   ```

4. Ask the new student to run the correct onboarding launcher.
5. Run the read-only access verifier.
6. Have the new student register through Azure Isekai.
7. Add or correct teacher class-roster membership if required.

Do not transfer ownership by changing only the Azure tag. Do not use progress
reset or class-roster removal as registration release.

## What Each Step Preserves

| Operation | Removes | Preserves |
| --- | --- | --- |
| Azure offboarding | Grader RBAC/Lighthouse and initialization tag | Student resources |
| Registration release | Email and subscription indexes | Progress and reports |
| Class removal | Teacher reporting membership | Registration, progress, Azure access |
| Progress reset | Current game progress | Registration, failures, reports, Azure access |

See [Subscription registration](../guides/subscription-registration.md) for
index guarantees.
