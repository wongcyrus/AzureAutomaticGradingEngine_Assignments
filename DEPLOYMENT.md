# Deployment Guide

## Prerequisites

- Azure subscription with Owner or Contributor permissions
- Node.js 22.19+ and npm
- .NET 8.0 SDK
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

# Read the deployment token from CDKTN and upload azure-isekai directly.
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

The account running `students:invite` needs permission to invite external
users and update group membership (for example, an appropriate Entra directory
role). Group assignment to an Enterprise Application may require Microsoft
Entra ID P1. Invited users receive no Azure subscription RBAC permissions. Student
subscription access is delegated separately to the grading identity.

### 4. Build and Deploy Function App

```bash
cd ../GraderFunctionApp
dotnet publish -c Release -o publish/

# Deploy to Azure Function App
func azure functionapp publish <your-function-app-name>
```

### 5. Deploy Test Library

```bash
cd ../AzureProjectTest
dotnet publish -r win-x64 -c Release

# Upload to storage using azcopy (command provided in deployment output)
azcopy copy "bin/Release/net8.0/win-x64/publish/*" "https://<storage>.blob.core.windows.net/testlib/<SAS-token>" --recursive
```

### 6. Update Student Access

Edit `Infrastructure/students.txt`, then rerun:

```bash
cd Infrastructure
npm run students:invite -- students.txt
```

The command is idempotent: existing guests and memberships are reused.

### 7. Verification

Test the deployment:
```bash
# Test function app
curl "https://<function-app>.azurewebsites.net/api/game-task?email=test@example.com&npc=Stella&game=azure-learning"

# Test game frontend (the hostname is a CDKTN output)
npx cdktn output AzureAutomaticGradingEngineGrader
```

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

The subscription must belong to the grading identity's Entra tenant, and
`projProd` must already exist. The script idempotently assigns:

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
from the student subscription.

### Pre-generate AI Messages

Populate message cache:
```bash
curl -X GET "https://<function-app>.azurewebsites.net/api/RefreshPreGeneratedMessages"
```

## Troubleshooting

### Common Issues

1. **Function App deployment fails**
   - Check .NET 8.0 is installed
   - Verify Azure CLI is logged in
   - Ensure sufficient permissions

2. **AI responses not working**
   - Verify Azure OpenAI endpoint and key
   - Check deployment name matches model
   - Ensure quota is available

3. **Game not loading**
   - Check static website is enabled
   - Verify CORS settings
   - Check browser console for errors

### Logs and Monitoring

- Function App logs: Azure Portal > Function App > Log Stream
- Application Insights: Azure Portal > Application Insights > Logs
- Storage logs: Azure Portal > Storage Account > Monitoring

## Scaling Considerations

- **Function App**: Configure auto-scaling based on load
- **Storage**: Use premium tier for high throughput
- **Azure OpenAI**: Monitor token usage and quotas
- **CDN**: Add Azure CDN for global game distribution
