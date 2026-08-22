# Deployment Guide

## Prerequisites

- Azure subscription with Owner or Contributor permissions
- Node.js 22.19+ and npm
- .NET SDK versions selected by `global.json` (the dev container installs both
  the pinned .NET 9 SDK and the .NET 8 target runtime/SDK)
- Azure CLI
- CDK Terrain (cdktn)

## Step-by-Step Deployment

### 1. Environment Setup

```bash
# Clone the repository
git clone --recurse-submodules <repository-url>
cd AzureAutomaticGradingEngine_Assignments

# Copy environment template
cp Infrastructure/.env.template Infrastructure/.env
```

Edit `.env` with your Azure OpenAI credentials:
```bash
AZURE_OPENAI_ENDPOINT=https://your-region.api.cognitive.microsoft.com/
AZURE_OPENAI_API_KEY=your-api-key
DEPLOYMENT_OR_MODEL_NAME=gpt-35-turbo
```

### 2. Azure Login

```bash
az login --use-device-code
az account set --subscription <your-subscription-id>
```

### 3. Infrastructure Deployment

```bash
npm run bootstrap
cd Infrastructure/

# Deploy infrastructure
npx cdktn deploy

# Install Static Web Apps API dependencies, refresh Entra/proxy settings, and
# deploy the frontend and API.
npm run frontend:deploy

# Create a roster with one email address per line. Include instructors who
# need to sign in, then invite/add everyone to the CDKTN-managed security group.
cp students.example.txt students.txt
npm run students:invite -- students.txt
```

This creates:
- Azure Function App
- User-assigned grading identity attached to the Function App
- Storage Account
- Application Insights
- Log Analytics Workspace
- Entra application and Enterprise Application
- `GradingEngineAssignmentStudents` Entra security group
- Group-only Enterprise Application assignment

CDKTN owns the infrastructure and initial Static Web Apps configuration. The
frontend deployment script owns the complete runtime settings map after
creation, including:

- `AADB2C_PROVIDER_CLIENT_ID`
- `AADB2C_PROVIDER_CLIENT_SECRET`
- `GameTaskFunctionUrl`
- `GraderFunctionUrl`
- `PassTaskFunctionUrl`
- `StudentRegistrationFunctionUrl`

Terraform intentionally ignores subsequent drift in the complete Static Web
Apps settings map so a later infrastructure deployment does not erase these
values. Always rerun `npm run frontend:deploy` after Function URLs, keys, or
Entra credentials change.

The account running `students:invite` needs permission to invite external
users and update group membership (for example, an appropriate Entra directory
role). Group assignment to an Enterprise Application may require Microsoft
Entra ID P1. Invited users receive no Azure subscription RBAC permissions. Student
subscription access is delegated separately to the grading identity.

### 4. Function and Test Deployment

`npx cdktn deploy` builds and zip-deploys `GraderFunctionApp` and publishes the
Windows NUnit runner to the Function App's storage file share. Ensure `dotnet`
is on `PATH`; in environments using the local installer:

```bash
PATH="$HOME/.dotnet:$PATH" npx cdktn deploy
```

### 5. Update Student Access

Edit `Infrastructure/students.txt`, then rerun:

```bash
cd Infrastructure
npm run students:invite -- students.txt
```

The command is idempotent: existing guests and memberships are reused.

### 6. Verification

Test the deployment:
```bash
# Anonymous access should redirect to Microsoft Entra sign-in.
curl -I "https://<static-web-app>/"

# Test game frontend (the hostname is a CDKTN output)
npx cdktn output AzureAutomaticGradingEngineGrader
```

Then sign in through a browser and verify
`/api/game-task?npc=Stella&game=azure-learning`. A plain anonymous `curl` cannot
exercise the student API because it has no Static Web Apps authentication
cookie.

## Post-Deployment Configuration

### Student Subscription Onboarding

Read these deployment outputs and distribute the values to students:

```bash
cd Infrastructure
npx cdktn output AzureAutomaticGradingEngineGrader
```

- `grading_identity_principal_id`
- `grading_identity_tenant_id`

Each student runs the following while signed in as an Owner or User Access
Administrator of the assignment subscription:

```bash
scripts/onboard-managed-identity.sh \
  -s <student-subscription-id> \
  -p <grading-identity-principal-id> \
  -t <grading-identity-tenant-id> \
  -e <azure-isekai-sign-in-email> \
  -i <instructor-user-object-id>
```

The subscription must belong to the grading identity's Entra tenant, but it
does not need to come from Azure Education Hub. Personal and other same-tenant
subscriptions are supported. `projProd` must already exist. The script
idempotently assigns:

- `Reader` on the subscription.
- `Website Contributor` on `projProd`.
- A `GradingStudentEmail` tag on `projProd`, binding registration to the
  student's authenticated Azure Isekai email.

The optional `-i` argument grants the same limited roles to an instructor user
for local test execution. Use an instructor-only principal; never use the
student access group.

Students then register only the subscription ID in Azure Isekai. RBAC changes
can take several minutes to propagate. No student service-principal password is
created or stored.

Alternatively, an instructor can write the same registration record after
onboarding:

```bash
cd Infrastructure
npm run students:import -- <student-email> <subscription-id>
```

The command is idempotent and refuses conflicting registrations, ownership
tags, or missing grader RBAC.

To revoke grading access, delete both role assignments for the grading identity
from the student subscription and remove the student's `Subscription` table
registration. Removing the Static Web Apps group membership only revokes game
sign-in; it does not revoke Azure RBAC.

Cross-tenant subscriptions cannot directly assign this managed identity
because it belongs to another tenant. Use Azure Lighthouse for explicit
cross-tenant delegation before registering such a subscription.

### Resetting One Student

The student's email is the partition key for game and result records. A full
game reset must remove all rows in that student's `GameStates` partition,
including `__active_task_lock__`, plus the intended `PassTests`, `FailTests`,
and test-result blobs. Do not delete the `Subscription` registration unless the
student must register a different subscription.

Deleting only an NPC state while leaving the lock row blocks future task
assignment. Application code deletes a matching state and lock atomically, but
manual Storage Explorer or CLI cleanup must include the lock explicitly.

### Pre-generate AI Messages

Populate message cache:
```bash
curl -X GET "https://<function-app>.azurewebsites.net/api/RefreshPreGeneratedMessages"
```

## Troubleshooting

### Common Issues

1. **Function App deployment fails**
   - Check the SDK selected by `global.json` and the .NET 8 target SDK/runtime
     are installed
   - Verify Azure CLI is logged in
   - Ensure sufficient permissions

2. **AI responses not working**
   - Verify Azure OpenAI endpoint and key
   - Check deployment name matches model
   - Ensure quota is available

3. **Game not loading**
   - Confirm the user belongs to `GradingEngineAssignmentStudents`
   - Check `/.auth/me` shows the expected authenticated email
   - Rerun `npm run frontend:deploy` to restore API dependencies and all proxy
     settings
   - A `404` from `/api/game-task` usually means the Static Web Apps API was
     deployed without its npm dependencies, not that the backend Function is
     missing

4. **Student can sign in but grading cannot access the subscription**
   - Confirm registration maps the same sign-in email to the subscription
   - Confirm `projProd` has the matching `GradingStudentEmail` tag
   - Confirm the grading identity has subscription `Reader` and scoped
     `Website Contributor`
   - Allow several minutes for new RBAC assignments to propagate

### Logs and Monitoring

- Function App logs: Azure Portal > Function App > Log Stream
- Application Insights: Azure Portal > Application Insights > Logs
- Storage logs: Azure Portal > Storage Account > Monitoring

## Scaling Considerations

- **Function App**: Configure auto-scaling based on load
- **Storage**: Use premium tier for high throughput
- **Azure OpenAI**: Monitor token usage and quotas
- **CDN**: Add Azure CDN for global game distribution
