# Teacher Class Performance Dashboard

The teacher dashboard at `/admin.html` provides roster-scoped class performance
and restricted operator support tools. It is separate from the student game,
registration, grading, and Azure onboarding workflows.

## What Changes for Students?

Nothing changes on the student side. Students continue to:

1. accept the Azure Isekai invitation and sign in;
2. onboard the grading managed identity from Azure Cloud Shell;
3. verify grader access;
4. register their subscription through `registration.html`;
5. play, submit tasks, and review progress through the existing pages.

Adding or removing a student from a teacher class does not notify the student
and does not modify their registration, subscription, marks, progress, failed
attempts, reports, Azure tags, RBAC, or Lighthouse delegation.

## Access Requirements

A teacher must satisfy all of the following:

- The account can sign in to Azure Isekai.
- The normalized sign-in email appears in the server-side `ADMIN_EMAILS`
  allowlist.
- The Static Web Apps managed API receives the trusted authenticated principal.
- The grading Function validates the proxy HMAC signature and independently
  checks the same operator allowlist.

The browser never receives a Function key or HMAC key. A signed-in student who
is not an operator receives HTTP `403` from teacher API operations.

## First-Time Setup

Classes are not created automatically from `students.txt`, Entra group
membership, subscription registrations, or game progress. This avoids global
student discovery and lets each teacher control the exact roster they may
inspect.

1. Open `https://<static-web-app-host>/admin.html`.
2. Enter a class name such as `CDCA 2026 Group A`.
3. Select **Create class**.
4. Keep the new class selected.
5. Prepare and import a CSV roster.
6. Wait for the overview, student table, and task analytics to load.

The selected class is only a dashboard selection. Students do not select or
join classes themselves.

## CSV Roster Format

The importer searches every CSV cell for email-shaped values. A header is
optional, and unrelated columns are ignored.

Recommended format:

```csv
email
student1@example.com
student2@example.com
```

Additional columns are also accepted:

```csv
student_id,name,email,group
10001,Student One,student1@example.com,A
10002,Student Two,student2@example.com,A
```

Import behavior:

- emails are trimmed and normalized to lowercase;
- duplicate emails are imported once;
- invalid and non-email cells are ignored by the browser;
- the browser sends at most 50 emails per request;
- the backend accepts at most 100 emails per transaction;
- repeated imports update the same roster rows and do not create duplicates;
- importing an email does not require the student to be registered yet.

After import, verify the class count and review the **Registration** column.
An unregistered student can remain on the roster and will begin showing
performance automatically after normal student registration and activity.

## Class Overview

The overview is calculated live from authoritative storage:

| Metric | Definition |
| --- | --- |
| Students | Number of emails in the selected class roster |
| Registered | Students with a consistent email and subscription index pair |
| Active tasks | Students with an authoritative active-task lock |
| Average marks | Mean of current pass-record marks across roster students |
| Completed tasks | Sum of distinct completed task names per student |
| Failed attempts | Number of retained failed-test attempt records |

The dashboard does not copy marks into a class cache. Refreshing reads current
registration, game state, pass, and failure rows.

## Student Performance Table

Each row shows:

- normalized student email;
- registration status;
- total marks;
- completed-task count;
- retained failed-attempt count;
- current active task;
- last recorded activity;
- detail and roster-removal actions.

Available controls:

- search by email or active-task name;
- filter registered or unregistered students;
- filter students with or without an active task;
- sort by email, marks, failures, or recent activity;
- export the currently filtered and sorted table to CSV.

CSV export happens in the browser from the already authorized response. No
public report URL or temporary blob is created.

## Student Detail

Select **Detail** to request one exact roster member. The backend rechecks:

1. the operator signature and allowlist;
2. ownership of the selected class;
3. membership of the exact student email.

The detail view contains registration status, subscription ID, marks, completed
tasks, failed attempts, active task, last activity, pass records, and up to 100
recent failed attempts.

## Task Analytics

Task analytics group the selected class's pass and failure records by task
name:

| Field | Meaning |
| --- | --- |
| Students attempted | Distinct roster students with a pass or failure record |
| Attempts | Pass and failure records combined |
| Passes | Recorded passing assertions |
| Failures | Retained failing assertions |
| Completion rate | Pass records divided by total pass and failure records |

Tasks with no recorded attempt do not appear. The table measures observed
grading records; it is not a prediction of task difficulty.

## Roster and Class Removal

**Remove** deletes one email only from `ClassMemberships`. **Delete class**
deletes the class definition and its roster metadata.

Both operations preserve:

- subscription registration;
- game state and marks;
- pass and failure records;
- reports and test-result blobs;
- Azure resources;
- `GradingStudentEmail`;
- direct RBAC and Azure Lighthouse delegation.

These controls are safe for reorganizing classes. They are not student
offboarding or registration reassignment.

## Registration Support

The support panel performs an exact email lookup against the atomic
registration indexes.

Registration release:

- requires typed email confirmation;
- verifies the email and subscription indexes agree;
- deletes both indexes in one Azure Table transaction;
- preserves progress, reports, Azure resources, tags, and access.

For subscription reassignment, follow the complete
[registration reassignment workflow](subscription-registration.md#reassignment-workflow).
Do not use roster removal as a substitute for registration release or Azure
offboarding.

## Message Cache Support

Operators can:

- read generated-message counts and hit statistics;
- regenerate the message cache;
- reset hit counters.

Regeneration may consume Azure OpenAI quota. The dashboard deliberately does
not expose the destructive clear-all-cache operation.

## Data Model and Isolation

```text
Classes
  PartitionKey: owner:<SHA-256(normalized-teacher-email)>
  RowKey:       <random class ID>

ClassMemberships
  PartitionKey: <class ID>
  RowKey:       student:<SHA-256(normalized-student-email)>
```

The class definition also stores its normalized owner email. Every class read
or write resolves the signed operator to the owner partition and verifies the
stored owner. One operator cannot open another operator's class by guessing a
class ID.

For each roster member, the backend performs bounded concurrent reads from:

- `SubscriptionRegistrations`;
- `GameStates`;
- `PassTests`;
- `FailTests`.

It never scans all registrations or all student progress.

## API and Azure Topology

Browser requests use `/api/teacher/*` on Azure Static Web Apps. These routes
run in the managed Node API deployed from `azure-isekai/api`.

The Azure Portal API mapping/linked-backend list is intentionally empty. The
managed API calls the separate `azureisekai2026` Function App through
server-side Function URLs and signed headers. Do not link the Function App
directly as a Bring Your Own API backend.

See:

- [API reference](../API.md)
- [Technical design](technical-design.md)
- [Deployment guide](../DEPLOYMENT.md#static-web-apps-api-mapping)

## Troubleshooting

### Dashboard says operator access is required

- Confirm the browser is signed in with the expected email.
- Check `/.auth/me`.
- Confirm the email appears in both Function App and Static Web Apps
  `ADMIN_EMAILS`.
- Rerun `npm run frontend:deploy` after changing the allowlist.

### No classes appear

Classes are teacher-owned and start empty. Create a class while signed in with
the same operator account that will manage it. Another operator's classes are
not visible.

### CSV imports zero students

- Confirm the file contains complete email addresses.
- Prefer one `email` column.
- Save the file as UTF-8 CSV or plain text.
- Remove spreadsheet formulas or display names such as
  `Student Name <student@example.com>`.

### Student shows “Not registered”

Class membership does not register a subscription. Ask the student to complete
normal Azure onboarding, verification, and web registration using the same
email imported into the roster.

### Marks or tasks are empty

- Confirm the roster email exactly matches the student's Azure Isekai sign-in.
- Confirm the student has submitted grading at least once.
- Refresh performance.
- Use the student's progress page to compare the same authoritative data.

### `/api/teacher/*` returns `404`

Check the managed API first:

```bash
curl -fsS "https://<static-web-app-host>/api/health"
```

Expected:

```json
{"status":"ok","service":"azure-isekai-api"}
```

Rerun `npm run frontend:deploy` if health fails. Do not add a portal linked
backend as a workaround.

### Backend storage fails

Verify the `Classes` and `ClassMemberships` tables exist, the newest Function
package is mounted, and `ClassPerformanceAdminFunctionUrl` is present in Static
Web Apps application settings.

## Operational Verification

After deployment:

1. Confirm `/api/health` returns the managed API identity.
2. Run `scripts/test-deployed-function.sh`.
3. Sign in as each configured teacher.
4. Create a temporary class.
5. Import one test email.
6. Confirm overview and student detail load.
7. Delete the temporary class.
8. Sign in as an ordinary student and confirm teacher APIs return `403`.

The deployed integration suite performs a self-cleaning version of the
temporary class lifecycle.
