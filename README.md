# Azure Automatic Grading Engine - Classroom Assignments

An automated grading system for Azure infrastructure assignments with gamified learning experience.

## Overview

This project provides automated assessment of student Azure infrastructure deployments through unit testing and gamified interactions. Students create Azure resources and receive immediate feedback through an RPG-style interface with NPC characters.

## Architecture

- **GraderFunctionApp**: Azure Functions backend for grading and game logic
- **azure-isekai**: RPG Maker game frontend for student interaction
- **Infrastructure**: CDK Terrain deployment code
- **packages/**: Git submodules containing shared provider bindings and Azure constructs
- **AzureProjectTest**: Unit test library for Azure resource validation

The grader uses one user-assigned managed identity to inspect explicitly
registered student subscriptions. Student service principals and passwords are
not created or stored.

## Quick Start

### Prerequisites

- Azure subscription with appropriate permissions
- Node.js 22.19+ and npm
- .NET 8.0 SDK
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
   CDKTN restricts sign-in to the `GradingEngineAssignmentStudents` Entra
   security group; the invitation script idempotently invites guests and adds
   existing or new users to that group.

3. **Build and Deploy Tests**
   ```bash
   cd AzureProjectTest
   dotnet publish -r win-x64 -c Release
   # Upload to Azure Function storage using provided azcopy command
   ```

## Student Assignment Tasks

Students must create the following Azure infrastructure:

1. **Networking**: 2 Virtual Networks in different regions with subnets, route tables, NSGs, and VNet peering
2. **Storage**: 2 Storage Accounts (Function App + Static Website) with containers, queues, tables
3. **Monitoring**: Application Insights with Log Analytics Workspace
4. **Compute**: Azure Function App with functions

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
- `GET /api/pass-task` - View completed tasks and scores

### Admin Functions
- `GET /api/pregeneratedmessagestats` - View message cache statistics
- `POST /api/pregeneratedmessagestats/reset` - Reset cache hit counts
- `GET /api/RefreshPreGeneratedMessages` - Refresh AI message cache

## Configuration

### Required Environment Variables

```bash
FUNCTION_APP_NAME=your-function-app-name
AZURE_OPENAI_ENDPOINT=https://your-region.api.cognitive.microsoft.com/
AZURE_OPENAI_API_KEY=your-api-key
DEPLOYMENT_OR_MODEL_NAME=gpt-35-turbo
```

### Student Subscription Onboarding

The instructor gets the identity values after deployment:

```bash
cd Infrastructure
npx cdktn output AzureAutomaticGradingEngineGrader
```

Each student must be an Owner or User Access Administrator on their assignment
subscription and must use the same Entra tenant as the grader. After creating
the assignment resource group, the student runs:

```bash
scripts/onboard-managed-identity.sh \
  -s <student-subscription-id> \
  -p <grading_identity_principal_id> \
  -t <grading_identity_tenant_id> \
  -e <azure-isekai-sign-in-email>
```

This grants `Reader` at subscription scope and `Website Contributor` only on
the `projProd` resource group. It also tags that resource group with the
student's sign-in email so another student cannot claim the subscription. The
script is idempotent. After RBAC propagates, the student signs in to Azure
Isekai with the same email and registers only the subscription ID.

## Testing Locally

Sign in with Azure CLI and run tests against an explicit subscription:

```bash
az login
dotnet run --project AzureProjectTest/AzureProjectTest.csproj --configuration Debug -- \
    --subscription=<subscription-id> --work=$(pwd)/testing --trace=local
```

## Performance Features

- **Message Caching**: Pre-generated AI responses for common scenarios
- **Hit Count Tracking**: Monitor cache effectiveness
- **Batch Processing**: Optimized message generation
- **Cross-NPC State Management**: Prevent task conflicts between NPCs

## Security

- Function-level authorization for all endpoints
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
