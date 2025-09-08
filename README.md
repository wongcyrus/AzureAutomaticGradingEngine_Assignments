# Azure Automatic Grading Engine - Classroom Assignments

An automated grading system for Azure infrastructure assignments with gamified learning experience.

## Overview

This project provides automated assessment of student Azure infrastructure deployments through unit testing and gamified interactions. Students create Azure resources and receive immediate feedback through an RPG-style interface with NPC characters.

## Architecture

- **GraderFunctionApp**: Azure Functions backend for grading and game logic
- **azure-isekai**: RPG Maker game frontend for student interaction
- **Infrastructure**: CDK-TF deployment scripts
- **AzureProjectTest**: Unit test library for Azure resource validation

## Quick Start

### Prerequisites

- Azure subscription with appropriate permissions
- Node.js 18+ and npm
- .NET 8.0 SDK
- Azure CLI

### Deployment

1. **Configure Environment**
   ```bash
   cp .env.template .env
   # Edit .env with your Azure OpenAI credentials
   ```

2. **Deploy Infrastructure**
   ```bash
   cd Infrastructure/
   npm install
   npm install --global cdktf-cli@latest
   az login --use-device-code
   cdktf deploy --auto-approve
   ```

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

### Service Principal Setup

Create a service principal with required permissions:

```bash
chmod +x scripts/create-sp-cloudshell.sh
scripts/create-sp-cloudshell.sh -s <subscriptionId>
```

## Testing Locally

Run tests with generated service principal credentials:

```bash
dotnet run --project AzureProjectTest/AzureProjectTest.csproj --configuration Debug -- \
    $(pwd)/testing/sp.json $(pwd)/testing trace ""
```

## Performance Features

- **Message Caching**: Pre-generated AI responses for common scenarios
- **Hit Count Tracking**: Monitor cache effectiveness
- **Batch Processing**: Optimized message generation
- **Cross-NPC State Management**: Prevent task conflicts between NPCs

## Security

- Function-level authorization for all endpoints
- Service principal with minimal required permissions
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
