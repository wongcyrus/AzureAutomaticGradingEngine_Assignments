# Student Access and Roster Management

Three independent data sets are often confused:

| Data set | Adds sign-in? | Adds class reporting? | Registers subscription? |
| --- | --- | --- | --- |
| `Infrastructure/students.txt` | Yes | No | No |
| Teacher dashboard CSV | No | Yes | No |
| Student web registration | No | No | Yes |

## Grant Application Sign-In

Add one email per line to `Infrastructure/students.txt`, then run:

```bash
cd Infrastructure
npm run students:invite -- students.txt
```

This invites external users when necessary and adds users to
`GradingEngineAssignmentStudents`. It grants no student-subscription RBAC.

## Add Teacher Reporting Scope

The teacher creates a class at `/admin.html` and imports a CSV roster. This
controls only which student partitions that teacher may view.

## Register a Student Subscription

The student completes Azure onboarding and submits the subscription ID through
`registration.html`. The backend verifies access and creates the atomic
registration indexes.

## Removing Access

- Remove class membership to stop teacher reporting visibility.
- Remove Entra group membership to stop application sign-in.
- Release registration to free the email/subscription pair.
- Run Azure offboarding to remove grader RBAC or Lighthouse delegation.

These operations are deliberately separate.

See [Teacher dashboard](../guides/teacher-dashboard.md),
[Subscription registration](../guides/subscription-registration.md), and
[Subscription reassignment](subscription-reassignment.md).
