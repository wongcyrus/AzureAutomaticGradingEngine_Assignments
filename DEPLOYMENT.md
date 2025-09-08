# Deployment Guide

## Prerequisites

- Azure subscription with Owner or Contributor permissions
- Node.js 18+ and npm
- .NET 8.0 SDK
- Azure CLI
- CDK for Terraform (cdktf)

## Step-by-Step Deployment

### 1. Environment Setup

```bash
# Clone the repository
git clone <repository-url>
cd AzureAutomaticGradingEngine_Assignments

# Copy environment template
cp .env.template .env
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
cd Infrastructure/
npm install
npm install --global cdktf-cli@latest

# Deploy infrastructure
cdktf deploy --auto-approve
```

This creates:
- Azure Function App
- Storage Account
- Application Insights
- Log Analytics Workspace
- Required IAM roles

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

### 6. Deploy Game Frontend

```bash
cd ../azure-isekai
npm install
npm run build

# Deploy to static website storage
az storage blob upload-batch -d '$web' -s dist/ --account-name <storage-account>
```

### 7. Verification

Test the deployment:
```bash
# Test function app
curl "https://<function-app>.azurewebsites.net/api/game-task?email=test@example.com&npc=Stella&game=azure-learning"

# Test game frontend
open https://<storage-account>.z13.web.core.windows.net/
```

## Post-Deployment Configuration

### Service Principal Setup

Create service principal for students:
```bash
chmod +x scripts/create-sp-cloudshell.sh
./scripts/create-sp-cloudshell.sh -s <subscription-id>
```

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
