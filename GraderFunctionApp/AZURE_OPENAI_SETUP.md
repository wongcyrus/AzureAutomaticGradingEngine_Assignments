# Azure OpenAI Configuration Guide

## Issue Resolution
The warning "Azure OpenAI configuration is incomplete" occurs when the required environment variables for Azure OpenAI are not properly configured.

## Required Configuration

### Environment Variables
You need to set the following environment variables in your `local.settings.json` file:

```json
{
  "Values": {
    "AZURE_OPENAI_ENDPOINT": "https://your-openai-resource.openai.azure.com/",
    "AZURE_OPENAI_API_KEY": "your-api-key-here",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

## Step-by-Step Setup

### 1. Create Azure OpenAI Resource
1. Go to [Azure Portal](https://portal.azure.com)
2. Create a new **Azure OpenAI** resource
3. Choose your subscription, resource group, and region
4. Select the pricing tier (Standard S0 recommended)

### 2. Deploy a Model
1. Go to your Azure OpenAI resource
2. Navigate to **Model deployments** or **Azure OpenAI Studio**
3. Create a new deployment:
   - **Model**: `gpt-4o-mini` (recommended) or `gpt-35-turbo`
   - **Deployment name**: Use the same name as the model (e.g., `gpt-4o-mini`)
   - **Version**: Latest available

### 3. Get Configuration Values
1. **Endpoint**: 
   - Go to your Azure OpenAI resource
   - Copy the **Endpoint** URL from the overview page
   - Format: `https://your-resource-name.openai.azure.com/`

2. **API Key**:
   - Go to **Keys and Endpoint** section
   - Copy **Key 1** or **Key 2**

3. **Deployment Name**:
   - Use the deployment name you created in step 2
   - Common names: `gpt-4o-mini`, `gpt-35-turbo`

### 4. Update Configuration
Update your `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "your-storage-connection-string",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_OPENAI_ENDPOINT": "https://your-resource-name.openai.azure.com/",
    "AZURE_OPENAI_API_KEY": "your-actual-api-key",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

## Production Deployment

### For Azure Functions App
Set these as Application Settings in your Azure Functions App:

1. Go to your Function App in Azure Portal
2. Navigate to **Configuration** > **Application settings**
3. Add the following settings:
   - `AZURE_OPENAI_ENDPOINT`
   - `AZURE_OPENAI_API_KEY`
   - `DEPLOYMENT_OR_MODEL_NAME`

### Using Azure Key Vault (Recommended)
For production, store the API key in Azure Key Vault:

1. Create an Azure Key Vault
2. Store the API key as a secret
3. Reference it in your Function App:
   ```
   AZURE_OPENAI_API_KEY=@Microsoft.KeyVault(SecretUri=https://your-keyvault.vault.azure.net/secrets/openai-api-key/)
   ```

## Verification

### Check Configuration
The AIService will log detailed information about missing configuration:
- ✅ Endpoint configured
- ✅ API Key configured  
- ✅ Deployment Name configured

### Test the Service
1. Deploy your Function App
2. Call the GameTaskFunction endpoint
3. Check the logs for successful AI rephrasing

### Expected Behavior
- **With Configuration**: Instructions are rephrased in a gamified style with emojis
- **Without Configuration**: Original instructions are returned with a warning log

## Troubleshooting

### Common Issues

1. **Invalid Endpoint Format**
   - Ensure the endpoint ends with `/`
   - Use the full Azure OpenAI endpoint, not the generic OpenAI endpoint

2. **Wrong API Key**
   - Use Azure OpenAI key, not OpenAI.com key
   - Ensure the key is from the correct resource

3. **Model Not Deployed**
   - Verify the model is deployed in Azure OpenAI Studio
   - Check the deployment name matches exactly

4. **Regional Availability**
   - Some models are only available in specific regions
   - Check [Azure OpenAI model availability](https://docs.microsoft.com/azure/cognitive-services/openai/concepts/models)

### Debug Logs
Enable detailed logging to see configuration status:
```json
{
  "Logging": {
    "LogLevel": {
      "GraderFunctionApp.Services.AIService": "Debug"
    }
  }
}
```

## Cost Considerations

### Token Usage
- The AI service is used for instruction rephrasing
- Each instruction uses approximately 100-200 tokens
- Responses are cached for 1 hour to reduce costs

### Pricing Tiers
- **gpt-4o-mini**: Most cost-effective option
- **gpt-35-turbo**: Good balance of cost and quality
- **gpt-4**: Higher quality but more expensive

### Cost Optimization
- Caching is implemented (1-hour cache)
- Only used when `rephrases=true` parameter is set
- Fallback to original instruction if service fails

## Security Best Practices

1. **Never commit API keys** to source control
2. **Use Key Vault** for production secrets
3. **Rotate keys regularly** (every 90 days recommended)
4. **Monitor usage** for unexpected spikes
5. **Set spending limits** in Azure OpenAI resource

## Support

If you continue to experience issues:
1. Check Azure OpenAI service health
2. Verify your subscription has access to Azure OpenAI
3. Review Azure OpenAI quotas and limits
4. Contact Azure support if needed
