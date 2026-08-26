# Azure Automatic Grading Engine - Classroom Assignments

An automated grading system for Azure infrastructure assignments with gamified learning experience.

## Overview

This project provides automated assessment of student Azure infrastructure deployments through unit testing and gamified interactions. Students create Azure resources and receive immediate feedback through an RPG-style interface with NPC characters.

## Architecture

- **GraderFunctionApp**: Azure Functions backend for grading and game logic
- **GraderFunctionApp.Tests**: NUnit unit and component tests for the Function App
- **azure-isekai**: RPG Maker game frontend for student interaction
- **Infrastructure**: CDK Terrain deployment code
- **packages/**: Git submodules containing shared provider bindings and Azure constructs
- **AzureProjectTestLib**: Public Git submodule containing the deployable grading suite
- **AzureProjectTest**: Hosted NUnit runner for Azure resource validation

The grader uses one user-assigned managed identity to inspect explicitly
registered student subscriptions. Student service principals and passwords are
not created or stored.

### Request and identity flow

1. The student signs in to Azure Static Web Apps with Microsoft Entra ID.
2. The Static Web Apps API reads the trusted authenticated principal and adds
   a short-lived HMAC signature over the student's email and exact backend
   request.
3. The Function App validates the signature and ignores browser-supplied
   `email`, form-email, and diagnostic `trace` identities.
4. The Function App resolves the signed email through its exact hashed index
   in the `SubscriptionRegistrations` table.
5. `DefaultAzureCredential` selects the Function App's user-assigned managed
   identity through `AZURE_CLIENT_ID`.
6. The grader runs only the tests for the student's active task against that
   subscription.

Frontend sign-in, subscription registration, and Azure RBAC are separate:
membership in the student sign-in group does not grant subscription access.
The onboarding script uses direct managed-identity RBAC for same-tenant
subscriptions and Azure Lighthouse delegation for cross-tenant subscriptions.
Both modes remain secretless and use the same managed identity at runtime.
Personal subscriptions are supported; Azure Education Hub is not required.
The onboarding tag proves control only when the student first registers. The
atomic database indexes are authoritative afterward, so changing the Azure tag
does not transfer or duplicate a registration.
See the [subscription registration workflow](docs/subscription-registration.md)
for student and teacher procedures.
For an architecture walkthrough with trust-boundary, registration, and grading
diagrams, see [Secretless multi-tenant Azure grading: technical design](docs/technical-design.md).

## Quick Start

### Prerequisites

- Azure subscription with appropriate permissions
- Node.js 24.19+ and npm
- .NET SDK versions selected by `global.json` and the dev container
- Azure CLI

### Deployment

1. **Configure Environment**
   ```bash
   cp Infrastructure/.env.template Infrastructure/.env
   # Edit .env with your Azure OpenAI credentials
   ```

2. **Deploy Infrastructure**
   ```bash
   git submodule update --init --recursive
   npm run bootstrap
   az login --use-device-code
   cd Infrastructure
   npx cdktn deploy
   npm run frontend:deploy
   cp students.example.txt students.txt
   # Add one instructor/student email per line, then:
   npm run students:invite -- students.txt
   ```

   The frontend deployment reads the Azure Static Web Apps token directly from
   CDKTN output. It does not use GitHub Actions or a GitHub token.
   Run `npm run frontend:deploy` after every infrastructure deployment that
   changes Function URLs or keys. The command installs API dependencies and
   refreshes all Function proxy and Entra app settings before deployment.
   CDKTN restricts sign-in to the `GradingEngineAssignmentStudents` Entra
   security group; the invitation script idempotently invites guests and adds
   existing or new users to that group.

`npx cdktn deploy` builds and deploys the Function App and hosted test runner.
After deployment, restart the Function App as described in
[DEPLOYMENT.md](DEPLOYMENT.md) so the newest run-from-package artifact is
mounted before students register.

## Student Assignment Tasks

Students must create the following Azure infrastructure:

1. **Networking**: Two regional VNets with subnets, route tables, NSGs, NAT,
   and peering.
2. **Storage**: Logic and static-website Storage accounts with the required
   container, queue, table, and website content.
3. **Monitoring**: Application Insights and Log Analytics.
4. **Compute**: A Windows Consumption Function App.

Students receive task requirements through Azure Isekai at assignment time.

## Game Features

- **NPC Characters**: AI-powered characters guide students through assignments
- **Task Management**: Sequential task assignment with progress tracking
- **Automated Grading**: Real-time validation of Azure resources
- **Score System**: Points awarded for completed tasks
- **Detailed Feedback**: XML test results for debugging failed deployments

## API Endpoints

### Core Functions
- `GET /api/game-task` - Get next task assignment
- `GET /api/grader` - Submit work for grading
- `GET /api/pass-task` - View player identity, progress, and failure history
- `POST /api/pass-task` - Reset progress while preserving failure history
- `POST /api/registration` - Atomically register one student and subscription

### Admin Functions
- `GET /api/pregeneratedmessagestats` - View message cache statistics
- `POST /api/pregeneratedmessagestats/reset` - Reset cache hit counts
- `POST /api/messages/refresh` - Refresh AI message cache

## Configuration

### Required Environment Variables

```bash
FUNCTION_APP_NAME=your-function-app-name
AZURE_OPENAI_ENDPOINT=https://your-region.api.cognitive.microsoft.com/
AZURE_OPENAI_API_KEY=your-api-key
DEPLOYMENT_OR_MODEL_NAME=gpt-35-turbo
```

### Student Subscription Onboarding

Use the role-specific guides:

- [Teacher](docs/onboarding-teacher.md)
- [Student in the same tenant](docs/onboarding-student-same-tenant.md), using
  the direct-RBAC launcher
- [Student in a different tenant](docs/onboarding-student-cross-tenant.md),
  using the Lighthouse launcher

See the [documentation index](docs/index.md) for all project guides.

### Assignment Regions

The grading suites define two assignment regions in
`AzureProjectTestLib/Constants.cs`:

- `Location1 = italynorth`: VNet 1, logic storage, App Service plan, and
  Function App.
- `Location2 = brazilsouth`: `projProd`, VNet 2, static-web storage, and
  Application Insights.

Existing `projProd` resource groups created in East Asia must be deleted and
recreated in Brazil South, then onboarded again so the ownership tag and scoped
grader permission are restored.

Validate both onboarding modes without changing Azure resources:

```bash
scripts/test-grading-access.sh
```

After onboarding, students can verify their selected subscription from Cloud
Shell without changing Azure resources:

```bash
curl -fsSL "https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw/cloudshell-verify-access.sh?v=$(date +%s)" \
  | bash
```

The verifier reports the detected tenant and expected access mode, checks the
ownership tag and grader permissions, and exits nonzero for incomplete or
mixed direct/Lighthouse configuration. Follow
[Verify student grading access](docs/verify-student-grading-access.md) for
required output, failure handling, and the student's final Azure Isekai grading
check.

## Testing Locally

Run the Function App unit tests:

```bash
dotnet test GraderFunctionApp.Tests/GraderFunctionApp.Tests.csproj
```

Collect Cobertura code coverage:

```bash
dotnet test GraderFunctionApp.Tests/GraderFunctionApp.Tests.csproj \
    --collect:"XPlat Code Coverage" \
    --settings GraderFunctionApp.Tests/coverlet.runsettings
```

The current suite contains 277 tests and covers 92.2% of Function App lines
and 82.4% of branches. Coverage is scoped to the `GraderFunctionApp` assembly
and excludes only generated Function SDK files
under `obj`; production source files remain included.

Sign in with Azure CLI and run tests against an explicit subscription:

```bash
az login
dotnet run --project AzureProjectTest/AzureProjectTest.csproj --configuration Debug -- \
    --subscription=<subscription-id> --work=$(pwd)/testing --trace=local
```

Run HTTP integration tests against the deployed Function App. Pass a grading
subscription as the third argument to execute all 40 Azure resource assertions
through the deployed Function:

```bash
az login
scripts/test-deployed-function.sh \
  GradingEngineAssignmentResourceGroup \
  azureisekai2026 \
  <subscription-id> \
  [<registered-student-email>]
```

The script obtains the host-level Function key through Azure CLI without
printing it and signs every request with the proxy-signing key. Without the
third argument, it runs twenty endpoint, authentication, and self-cleaning
lifecycle tests. With a subscription, it also runs the complete Azure resource suite
through the Function and persists that grading result under the integration
test identity. The optional fourth argument signs requests as a registered
student instead of `deployment-test@example.com`.

## Performance Features

- **Message Caching**: Pre-generated AI responses use deterministic persisted keys that remain valid across restarts and scale-out
- **Hit Count Tracking**: Monitor cache effectiveness
- **Batch Processing**: Optimized message generation
- **Observable batch outcomes**: Retry exhaustion is reported as failure rather
  than counted as a successful generation
- **Atomic task ownership**: One fixed lock row per student is acquired in the
  same Azure Table transaction as task assignment
- **Concurrency-safe completion**: ETag-conditional transactions prevent
  duplicate or delayed grading requests from overwriting newer task state

## Game-state concurrency

All of a student's game-state and active-lock rows share the student's email
as their Azure Table partition key. Assignment atomically adds
`__active_task_lock__` and updates the winning NPC state:

- simultaneous requests to different NPCs produce one `TASK_ASSIGNED` and one
  `BUSY_WITH_OTHER_NPC`;
- duplicate requests to the same NPC return the same active task;
- completion updates the state and deletes the matching lock atomically;
- first-time initialization is create-only, so it cannot overwrite an
  assignment created by another request;
- delayed grading writes use ETags and cannot clear or rewrite a newer task.

## Security

- Function-level authorization for all endpoints
- Short-lived HMAC-signed SWA-to-Function identity assertions
- Function keys are forwarded in headers and are insufficient without a valid
  signed identity
- Operator endpoints additionally require the signed email to appear in the
  `ADMIN_EMAILS` allowlist; ordinary students receive `403`
- User-assigned managed identity with student-delegated, least-privilege RBAC
- No student application secrets are stored
- SAS URLs for secure test result access
- Input validation and sanitization

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes with appropriate tests
4. Submit a pull request

## License

This project is licensed under the MIT License. See LICENSE file for details.

## Support

For issues and questions:
- Create an issue in the GitHub repository
- Contact the development team
- Review the troubleshooting documentation

## Acknowledgments

Developed by [Cyrus Wong](https://www.linkedin.com/in/cyruswong) (Microsoft MVP Azure) in association with Microsoft Next Generation Developer Relations Team.

Project collaborators: Kwok Hau Ling, Lau Hing Pui, and Xu Yuan from IT114115 Higher Diploma in Cloud and Data Centre Administration.
