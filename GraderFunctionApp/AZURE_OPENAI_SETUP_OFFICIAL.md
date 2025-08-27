# Azure OpenAI Setup Guide - Official SDK Pattern

## Overview
This guide follows the official Azure OpenAI SDK documentation to properly configure the GraderFunctionApp for AI-powered instruction rephrasing.

## Prerequisites
- Azure subscription with Azure OpenAI access
- .NET 8.0 SDK
- Azure Functions Core Tools

## Step 1: Create Azure OpenAI Resource

### 1.1 Create Resource in Azure Portal
1. Go to [Azure Portal](https://portal.azure.com)
2. Click "Create a resource"
3. Search for "Azure OpenAI"
4. Click "Create"
5. Fill in the details:
   - **Subscription**: Your Azure subscription
   - **Resource Group**: Create new or use existing
   - **Region**: Choose a region that supports your desired model (e.g., East US, West Europe)
   - **Name**: Choose a unique name (e.g., `hkiitazureai`)
   - **Pricing Tier**: Standard S0

### 1.2 Wait for Deployment
- Deployment typically takes 5-10 minutes
- You'll receive a notification when complete

## Step 2: Deploy a Model

### 2.1 Access Azure OpenAI Studio
1. Go to your Azure OpenAI resource
2. Click "Go to Azure OpenAI Studio" or visit [oai.azure.com](https://oai.azure.com)
3. Select your resource

### 2.2 Create Model Deployment
1. Navigate to **Deployments** in the left menu
2. Click **+ Create new deployment**
3. Configure the deployment:
   - **Model**: Select `gpt-4o-mini` (recommended for cost-effectiveness)
   - **Model version**: Use the latest available
   - **Deployment name**: `gpt-4o-mini` (keep it simple)
   - **Content filter**: Default
   - **Tokens per minute rate limit**: 30K (adjust based on needs)

### 2.3 Verify Deployment
- Ensure the deployment status shows "Succeeded"
- Note the deployment name for configuration

## Step 3: Get Configuration Values

### 3.1 Get Endpoint URL
1. Go to your Azure OpenAI resource in Azure Portal
2. Navigate to **Keys and Endpoint**
3. Copy the **Endpoint** value
4. Format should be: `https://your-resource-name.openai.azure.com/`

### 3.2 Get API Key
1. In the same **Keys and Endpoint** section
2. Copy **Key 1** (32-character string)
3. Keep this secure - treat it like a password

### 3.3 Note Deployment Name
- Use the deployment name you created in Step 2.2
- Typically: `gpt-4o-mini`

## Step 4: Configure the Application

### 4.1 Update local.settings.json
Based on the official documentation pattern, update your configuration:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "your-storage-connection-string",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_OPENAI_ENDPOINT": "https://your-resource-name.openai.azure.com/",
    "AZURE_OPENAI_API_KEY": "your-32-character-api-key",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

### 4.2 Example Configuration
Using the documentation example:
```json
{
  "Values": {
    "AZURE_OPENAI_ENDPOINT": "https://hkiitazureai.cognitiveservices.azure.com/",
    "AZURE_OPENAI_API_KEY": "abcd1234567890abcd1234567890abcd",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

## Step 5: Test the Configuration

### 5.1 Build and Run
```bash
cd GraderFunctionApp
dotnet build
dotnet run
```

### 5.2 Test with Diagnostic Endpoint
```bash
# Test the diagnostic endpoint
curl -X GET "http://localhost:7071/api/diagnostic"
```

### 5.3 Expected Diagnostic Results
```json
{
  "success": true,
  "data": {
    "environment": {
      "azureOpenAIEndpoint": "Configured",
      "azureOpenAIApiKey": "Configured",
      "deploymentName": "gpt-4o-mini"
    },
    "tests": {
      "AzureOpenAI_Direct": {
        "status": "Success",
        "response": "Hello, this is a test!",
        "responseLength": 23
      }
    }
  }
}
```

## Step 6: Verify AI Rephrasing

### 6.1 Test GameTask Function
```bash
# Call the GameTask function with rephrasing enabled
curl -X GET "http://localhost:7071/api/GameTaskFunction?email=test@example.com&npc=guide&game=azure"
```

### 6.2 Expected Behavior
- **Without AI**: Instructions in plain English
- **With AI**: Instructions rephrased in gamified style with emojis and Chinese translation

## Troubleshooting Common Issues

### Issue 1: 401 Unauthorized
**Cause**: Invalid API key
**Solution**: 
- Verify API key is correct (32 characters)
- Ensure you're using Azure OpenAI key, not OpenAI.com key
- Check if key has been regenerated

### Issue 2: 404 Not Found
**Cause**: Deployment doesn't exist
**Solution**:
- Verify deployment name matches exactly
- Check deployment status in Azure OpenAI Studio
- Ensure deployment is in "Succeeded" state

### Issue 3: 403 Forbidden
**Cause**: Access denied or quota exceeded
**Solution**:
- Verify subscription has Azure OpenAI access
- Check quota limits in Azure Portal
- Ensure resource is in correct region

### Issue 4: Invalid Endpoint Format
**Cause**: Wrong endpoint URL format
**Solution**:
- Use format: `https://resource-name.openai.azure.com/`
- Don't use `cognitiveservices.azure.com` (old format)
- Ensure HTTPS and trailing slash

## SDK Code Pattern (Reference)

The application follows this official pattern:

```csharp
using OpenAI.Chat;
using Azure;
using Azure.AI.OpenAI;

var endpoint = new Uri("https://hkiitazureai.cognitiveservices.azure.com/");
var deploymentName = "gpt-4o-mini";
var apiKey = "your-api-key";

AzureOpenAIClient azureClient = new(
    endpoint,
    new AzureKeyCredential(apiKey));
ChatClient chatClient = azureClient.GetChatClient(deploymentName);

var requestOptions = new ChatCompletionOptions()
{
    MaxOutputTokenCount = 800,
    Temperature = 0.9f,
    FrequencyPenalty = 0,
    PresencePenalty = 0
};

List<ChatMessage> messages = new List<ChatMessage>()
{
    new SystemChatMessage("You are a helpful assistant."),
    new UserChatMessage("Your message here"),
};

var response = await chatClient.CompleteChatAsync(messages, requestOptions);
Console.WriteLine(response.Value.Content[0].Text);
```

## Production Deployment

### Azure Function App Settings
```bash
az functionapp config appsettings set \
  --name YourFunctionApp \
  --resource-group YourResourceGroup \
  --settings \
    AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/" \
    AZURE_OPENAI_API_KEY="@Microsoft.KeyVault(SecretUri=https://vault.vault.azure.net/secrets/openai-key/)" \
    DEPLOYMENT_OR_MODEL_NAME="gpt-4o-mini"
```

### Security Best Practices
1. **Use Key Vault** for API keys in production
2. **Rotate keys** regularly (every 90 days)
3. **Monitor usage** to detect anomalies
4. **Set spending alerts** to control costs

## Cost Optimization

### Model Selection
- **gpt-4o-mini**: Most cost-effective, good quality
- **gpt-35-turbo**: Balanced cost and performance
- **gpt-4**: Highest quality, most expensive

### Usage Optimization
- **Caching**: Responses cached for 1 hour
- **Conditional Usage**: Only when `rephrases=true`
- **Fallback**: Returns original on failure
- **Token Limits**: Max 800 tokens per response

## Support Resources

- [Azure OpenAI Documentation](https://docs.microsoft.com/azure/cognitive-services/openai/)
- [Azure OpenAI Studio](https://oai.azure.com)
- [Azure Status Page](https://status.azure.com/)
- [Pricing Calculator](https://azure.microsoft.com/pricing/calculator/)

## Monitoring

### Key Metrics
- Success rate of AI rephrasing
- Average response time
- Token usage and costs
- Error rates by type

### Recommended Alerts
- Error rate > 5% over 10 minutes
- Response time > 30 seconds
- Daily token usage exceeds budget
