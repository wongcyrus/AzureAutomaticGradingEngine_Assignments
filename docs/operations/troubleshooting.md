# Troubleshooting

## Static Web App or Managed API

1. Confirm the production environment is `Ready`.
2. Check the managed API:

   ```bash
   curl -fsS "https://<static-web-app-host>/api/health"
   ```

3. Confirm the user belongs to `GradingEngineAssignmentStudents`.
4. Check `/.auth/me` shows the expected normalized email.
5. Rerun `npm run frontend:deploy`.

Do not link the grading Function App in the portal API mapping. See
[Static Web Apps API topology](../architecture/static-web-apps-api.md).

## Teacher Dashboard

- Confirm the teacher is present in both deployed `ADMIN_EMAILS` settings.
- Remember that classes are owner-specific and initially empty.
- Confirm `Classes` and `ClassMemberships` exist.
- Confirm `ClassPerformanceAdminFunctionUrl` is present in Static Web Apps
  settings.

See [Teacher dashboard troubleshooting](../guides/teacher-dashboard.md#troubleshooting).

## Student Can Sign In but Grading Cannot Access Azure

Ask the student to run the maintained verifier:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" |
  bash
```

Confirm:

- selected subscription and tenant relationship are correct;
- `GradingStudentEmail` matches the Azure Isekai sign-in;
- same-tenant access uses direct RBAC;
- cross-tenant access uses both Lighthouse delegations;
- role propagation has completed.

See [Verify grading access](../guides/verify-grading-access.md).

## Registration Conflict

Use exact registration lookup in the teacher dashboard. Do not change only the
Azure tag. For a real transfer, use the
[subscription reassignment runbook](subscription-reassignment.md).

## Function Package Appears Stale

Restart the Function App after CDK Terrain deployment and verify the mounted
`appsettings.json`. A successful upload alone does not prove the running host
mounted the newest `WEBSITE_RUN_FROM_PACKAGE` artifact.

## Azure OpenAI or Message Cache

- Verify endpoint, key, deployment name, and quota.
- Check Function Application Insights.
- Refresh generated messages only after deploying changed task instructions.

## Logs

- Function backend: `azure-isekai-grader-insights`
- Managed web API: `azure-isekai-web-insights`
- Azure Portal Function App log stream
- Storage account monitoring

## Escalation Evidence

Collect the failing URL path, HTTP status, timestamp, signed-in email, relevant
Application Insights operation ID, and verifier output. Never copy Function
keys, signing keys, or full key-bearing URLs into tickets.
