# Deployment Guide

## Prerequisites

- Azure subscription with Owner or Contributor permissions
- Node.js 24.19+ and npm
- .NET SDK versions selected by `global.json` (the dev container installs both
  the pinned .NET 10 SDK and the .NET 8 target runtime/SDK)
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

The recursive clone includes the public grading-test source. It requires no
package credentials. For an owner deployment using the private replacement
suite, follow [Public and private grading tests](docs/private-tests.md).

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

# Seed idempotent NPC and Easter egg baseline data.
npm run storage:seed

# Install Static Web Apps API dependencies, refresh Entra/proxy settings, and
# deploy the frontend and API.
npm run frontend:deploy

# Create a roster with one email address per line. Include instructors who
# need to sign in, then invite/add everyone to the CDKTN-managed security group.
cp students.example.txt students.txt
npm run students:invite -- students.txt
```

The command above uses the public `AzureProjectTestLib` submodule. Public
deployments require no package credentials.

For an owner deployment using the private grading package, set a classic GitHub
token with `read:packages` and `repo` access, then wrap the CDKTF command:

```bash
export GITHUB_PACKAGES_TOKEN="<token>"
export GITHUB_PACKAGES_USER="wongcyrus"
export GITHUB_PACKAGES_OWNER="wongcyrus"

cd ..
scripts/with-private-tests.sh \
  bash -lc 'cd Infrastructure && npx cdktn deploy --auto-approve'
```

The wrapper sets `UsePrivateTests=true` for every nested Function and hosted
runner build and deletes its temporary authenticated NuGet configuration on
exit. A normal `gh auth token` is insufficient unless it explicitly includes
`read:packages`. See [Public and private grading tests](docs/private-tests.md)
for package publication and version updates. Owner-only build logs identify the
selected suite as `Public` or `Private`.

The current owner deployment uses
`WongCyrus.AzureProjectTestLib.Private` version `1.0.6`. It contains 33 game
tasks and 40 Azure assertions. After changing task instructions, deploy the new
package before refreshing pre-generated messages; otherwise the refresh uses
the previous assembly's task strings.

Fork maintainers can use their own private package by setting
`PrivateTestPackageId` and `PrivateTestPackageVersion` in
`Directory.Build.props`, then setting `GITHUB_PACKAGES_OWNER` to the GitHub
account or organization that owns that package. See
[Public and private grading tests](docs/private-tests.md) for the complete
setup.

`npx cdktn deploy` creates the Azure tables but does not populate table
entities. Run `npm run storage:seed` after every clean deployment to import the
version-controlled NPC personalities and Easter egg links from
`Infrastructure/data/`. The command merges by partition and row key, so it is
safe to rerun. It does not create or restore student registrations, progress,
passes, failures, or reports.

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
- `GRADER_PROXY_SIGNING_KEY`

All Azure resources owned by the stack use
`GradingEngineAssignmentResourceGroup` and deterministic names:

| Resource | Name |
| --- | --- |
| Function App | `azureisekai2026` |
| Function storage | `azureisekaigrading2026` |
| App Service plan | `azure-isekai-grading-plan` |
| Grading identity | `azure-isekai-grader-identity` |
| Static Web App | `azure-isekai-grading-web` |
| Log Analytics workspace | `azure-isekai-grading-workspace` |
| Function Application Insights | `azure-isekai-grader-insights` |
| Web Application Insights | `azure-isekai-web-insights` |

Both Application Insights resources use the shared explicit Log Analytics
workspace. This prevents Azure from creating separate `ai_*_managed` resource
groups. Microsoft Entra applications and groups remain tenant-scoped because
they are not Azure Resource Manager resources.

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

Before deployment, validate every deployment surface:

```bash
dotnet test AzureProjectGrader.sln --configuration Release
npm run build
npm test
npm run synth
terraform -chdir=Infrastructure/cdktf.out/stacks/AzureAutomaticGradingEngineGrader validate
```

The Function uses `Microsoft.ApplicationInsights.WorkerService` 3.x.
`Microsoft.Azure.Functions.Worker.ApplicationInsights` currently targets the
older Application Insights API and is binary-incompatible with 3.x; do not add
`ConfigureFunctionsApplicationInsights()` unless the package versions become
compatible.

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

Run the external Function integration suite after every deployment. Include
the grading subscription so the check executes all 40 Azure resource tests
inside the deployed Function:

```bash
cd ..
scripts/test-deployed-function.sh \
  GradingEngineAssignmentResourceGroup \
  azureisekai2026 \
  <subscription-id> \
  [<registered-student-email>]
```

The runner retrieves a host key with the current Azure CLI identity and passes
it through the `x-functions-key` header. It also retrieves the proxy-signing
key and creates the same short-lived identity signatures as Static Web Apps.
Keys are never written to source, command output, or test-result files. The
subscription argument adds one full grading test to the seven non-destructive
HTTP checks and writes its NUnit result through the normal grading storage
path. The optional fourth argument runs those checks with the registered
student's signed identity. Omit the subscription when only endpoint and
authentication checks are required.

Function URL outputs and the proxy-signing-key output are marked sensitive in
Terraform. The frontend deployment script is the only supported consumer of
those outputs; proxy code removes URL query keys before forwarding and does
not log backend URLs.

## Post-Deployment Configuration

### Student Subscription Onboarding

Onboarding is split into role-specific guides:

- [Teacher](docs/onboarding-teacher.md)
- [Student in the same tenant](docs/onboarding-student-same-tenant.md)
- [Student in a different tenant](docs/onboarding-student-cross-tenant.md)

See the [documentation index](docs/index.md) for all project guides.

After a clean destroy and redeploy, retrieve the new
`grading_identity_principal_id` from the CDKTF outputs. Update the embedded
principal ID in `scripts/cloudshell-onboard.sh`,
`scripts/cloudshell-offboard.sh`, and
`scripts/cloudshell-verify-access.sh`, then publish all three updated files to
the maintained onboarding Gist before students onboard again. Update the
embedded tenant ID in all three files as well if the deployment tenant changed.

Students must run the offboarding launcher before the old stack is destroyed
and before the Gist is changed to the new principal ID. The same launcher
automatically removes same-tenant direct RBAC or different-tenant Azure
Lighthouse access while preserving `projProd`.

### Resetting One Student

Use the reset script from the repository root:

```bash
scripts/reset-student-game.sh student@example.com
```

It reports the matching rows and blobs before requesting confirmation. Use
`--yes` for non-interactive operation, and `--resource-group` or
`--storage-account` when resetting a non-default deployment. The script removes
all rows in that student's `GameStates` partition, including
`__active_task_lock__`, plus their `PassTests`. It preserves `FailTests`,
test-result blobs, and the `Subscription` registration by default. Use
`--purge-failures` or `--purge-results` only when that retained history must
also be removed.
Close the student's active game client before resetting; the script retries
concurrent writes briefly and fails rather than reporting a false success.

Students can perform the same progress-only reset from `pass-task.html`. The
authenticated reset cannot target another student's partition.

Deleting only an NPC state while leaving the lock row blocks future task
assignment. Application code deletes a matching state and lock atomically, but
manual Storage Explorer or CLI cleanup must include the lock explicitly.

### Pre-generate AI Messages

`MessageRefreshTimerFunction` refreshes the cache daily at 02:00 UTC. After a
grading-task deployment, an owner can invoke that timer immediately without
printing the host master key:

```bash
master_key="$(
  az functionapp keys list \
    --resource-group GradingEngineAssignmentResourceGroup \
    --name azureisekai2026 \
    --query masterKey \
    --output tsv
)"
curl -fsS \
  --request POST \
  "https://azureisekai2026.azurewebsites.net/admin/functions/MessageRefreshTimerFunction" \
  --header "x-functions-key: $master_key" \
  --header "Content-Type: application/json" \
  --data '{"input":null}'
unset master_key
```

NPC message rows use deterministic SHA-256 key components. Cached messages
therefore remain readable after Function restarts and across scaled-out
instances. Existing rows created with process-randomized `GetHashCode()` keys
cannot be migrated reliably and may be removed before refreshing the cache.
Task text is part of each deterministic key: deploy changed attributes first,
then refresh so new messages are generated from the active assembly.

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
   - Ask the student to run:
     ```bash
     curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
       | bash
     ```
   - Confirm it reports the correct tenant mode, matching
     `GradingStudentEmail` tag, and complete direct RBAC or Lighthouse access
   - Treat Lighthouse on a same-tenant subscription, or direct grader RBAC on a
     cross-tenant subscription, as stale or incorrect configuration
   - Allow several minutes for new RBAC assignments to propagate
   - Follow [Verify student grading access](docs/verify-student-grading-access.md)
     and do not consider onboarding complete until the student successfully
     grades the initial `projProd` task through Azure Isekai

### Logs and Monitoring

- Function App logs: Azure Portal > Function App > Log Stream
- Application Insights: Azure Portal > Application Insights > Logs
- Storage logs: Azure Portal > Storage Account > Monitoring

## Scaling Considerations

- **Function App**: Configure auto-scaling based on load
- **Storage**: Use premium tier for high throughput
- **Azure OpenAI**: Monitor token usage and quotas
- **CDN**: Add Azure CDN for global game distribution
