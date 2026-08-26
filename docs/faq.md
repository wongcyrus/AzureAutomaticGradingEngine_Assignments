# Frequently Asked Questions

## Teacher Dashboard and Classes

### Why is no class selected after deployment?

Classes start empty by design. A teacher creates a class and imports the exact
student roster. The system does not infer a class from Entra groups,
`students.txt`, registrations, or storage scans.

### How do I create the first class?

Open `/admin.html`, enter a class name, select **Create class**, and import a
CSV containing student emails.

### Does a student need to choose a class?

No. Class membership is teacher-side reporting metadata. Students continue
using the same game, registration, grading, and progress pages.

### Does importing a student register their subscription?

No. Importing only adds an email to `ClassMemberships`. The student must
complete Azure onboarding and web registration independently.

### Can I import students before they register?

Yes. They appear as **Not registered** until a consistent registration exists.

### What CSV format is supported?

A single `email` column is recommended. The importer also accepts additional
columns and searches cells for complete email addresses.

### Are duplicate CSV rows a problem?

No. Emails are normalized and roster upserts are idempotent.

### Can two teachers see the same class?

Not automatically. A class belongs to the operator who created it. Other
operators cannot read it, even if they know its ID. Each teacher can create a
separate class with the same students.

### What happens when I remove a student from a class?

Only roster membership is removed. Registration, marks, tasks, failures,
reports, Azure resources, tags, RBAC, and Lighthouse access are preserved.

### What happens when I delete a class?

The class definition and roster rows are deleted. Student data and Azure access
are preserved.

### Is class performance cached?

No. The dashboard calculates it from current registration, game-state, pass,
and failure rows. This avoids stale marks after grading or reset.

### Why is an unattempted task missing from task analytics?

Task analytics shows observed pass and failure records. A task with no grading
record has no class analytics row yet.

### What does completion rate mean?

It is passing records divided by total passing and failing records for that
task within the selected roster. It is not the percentage of the full course
completed.

### Does CSV export expose a public file?

No. The browser creates the CSV from the authorized response. No public blob or
download URL is created.

## Student Experience

### Did the class dashboard change the student workflow?

No. Students still sign in, onboard grader access, register a subscription,
play, submit grading, and view progress exactly as before.

### Can class membership affect marks or grading?

No. Grading resolves the signed student's registration and active task. It does
not consult `Classes` or `ClassMemberships`.

### Can a teacher reset a student's progress from the class table?

No. The class table is read-only for performance. Student progress reset
remains a separate authenticated player operation.

### Can students see the class roster?

No. Teacher APIs require the operator allowlist at both proxy and backend.

### What if the roster email differs from the student's sign-in email?

The dashboard reads the wrong or empty student partition. Remove the incorrect
roster entry and import the exact normalized Azure Isekai sign-in email.

## Registration and Azure Access

### Is class removal the same as registration release?

No. Class removal changes reporting metadata only. Registration release
atomically deletes the email/subscription index pair.

### Does registration release remove Azure access?

No. It preserves RBAC, Lighthouse delegation, tags, resources, progress, and
reports. Follow the complete offboarding/reassignment workflow when ownership
changes. See [Subscription reassignment](operations/subscription-reassignment.md).

### Does progress reset release a subscription?

No. Progress reset preserves subscription registration and Azure access.

### Can changing `GradingStudentEmail` transfer a registration?

No. The tag proves the initial claim only. The atomic database indexes are
authoritative afterward.

### Why are there two registration rows?

Azure Table Storage has no unique secondary index. The email index resolves a
student to a subscription, while the subscription index prevents another
student from claiming the same subscription. Both are changed atomically.

## Authentication and Security

### Who can open the teacher dashboard?

Only signed-in accounts listed in `ADMIN_EMAILS`. The managed Static Web Apps
API checks first, and the grading Function checks again.

### Is being in the student sign-in group enough to become a teacher?

No. The sign-in group permits application access. `ADMIN_EMAILS` separately
grants operator privileges.

### Are Function keys exposed to the browser?

No. Function URLs, Function keys, and the HMAC key are server-side Static Web
Apps settings.

### Can an operator change the email query to inspect any student?

Class performance endpoints verify class ownership and exact roster membership.
Registration support deliberately permits exact lookup for authorized
operators but does not provide global browsing.

## Static Web Apps API

### Why is Azure Portal API mapping empty?

The project uses the Static Web Apps managed Node API. Portal API mapping is
for Bring Your Own API backends. An empty `linkedBackends` collection is
expected and does not mean the API is disabled.

See [Static Web Apps API topology](architecture/static-web-apps-api.md).

### Should I link `azureisekai2026` as the Static Web Apps API?

No. Linking it would replace the managed Node proxy and bypass the current
trusted-principal-to-HMAC flow.

### How do I prove the managed API is deployed?

Run:

```bash
curl -fsS "https://<static-web-app-host>/api/health"
```

Expected:

```json
{"status":"ok","service":"azure-isekai-api"}
```

`npm run frontend:deploy` performs this check and fails if it does not pass.

### Why does an anonymous teacher API request redirect to `/login`?

Static Web Apps authentication protects the route before the managed API runs.
After sign-in, a student receives `403`; an allowlisted teacher can continue.

## Deployment and Troubleshooting

### Which command deploys the teacher dashboard and managed API?

From `Infrastructure`:

```bash
npm run frontend:deploy
```

This installs API dependencies, updates server-side settings, deploys the
frontend and managed API, and verifies `/api/health`.

### Which command deploys class tables and the grading Function?

```bash
npx cdktn deploy --auto-approve
az functionapp restart \
  --resource-group GradingEngineAssignmentResourceGroup \
  --name azureisekai2026
```

The restart ensures the newest run-from-package artifact is mounted.

### Which tables are required for class performance?

- `Classes`
- `ClassMemberships`
- `SubscriptionRegistrations`
- `GameStates`
- `PassTests`
- `FailTests`

### What should I check after deployment?

1. Static Web Apps production environment is `Ready`.
2. `/api/health` returns `200`.
3. Required server-side settings are present.
4. `Classes` and `ClassMemberships` exist.
5. The deployed integration suite passes.
6. A teacher can complete the temporary class lifecycle.
7. An ordinary student receives `403`.

Use the [deployment guide](operations/deployment.md) and centralized
[troubleshooting runbook](operations/troubleshooting.md) for commands and
failure recovery.

### Where is the detailed dashboard guide?

See [Teacher class performance dashboard](guides/teacher-dashboard.md).
