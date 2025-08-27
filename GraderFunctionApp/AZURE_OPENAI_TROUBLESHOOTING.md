# Azure OpenAI Error Troubleshooting Guide

## Error: "Error occurred while rephrasing instruction with Azure OpenAI"

This error indicates that the Azure OpenAI service is being called but encountering an exception during the API request.

## Enhanced Diagnostics

I've added enhanced error handling and logging to help identify the specific issue. The new version will log:

- **Configuration Status**: Which environment variables are set/missing
- **API Request Details**: Endpoint, deployment name, message count
- **Specific Error Types**: Different error types with targeted messages
- **Response Validation**: Checks for empty or null responses

## Common Causes & Solutions

### 1. **Invalid API Key**
**Error Pattern**: `RequestFailedException` with status 401
**Solution**: 
- Verify your API key is correct
- Check if the key has expired
- Ensure you're using the Azure OpenAI key, not OpenAI.com key

```bash
# Test your API key with curl
curl -H "api-key: YOUR_API_KEY" \
     -H "Content-Type: application/json" \
     "https://your-resource.openai.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-02-15-preview" \
     -d '{"messages":[{"role":"user","content":"Hello"}],"max_tokens":10}'
```

### 2. **Wrong Endpoint URL**
**Error Pattern**: `UriFormatException` or `HttpRequestException`
**Common Issues**:
- Missing `https://` prefix
- Wrong domain (should be `.openai.azure.com`)
- Missing trailing slash

**Correct Format**: `https://your-resource-name.openai.azure.com/`

### 3. **Model/Deployment Not Found**
**Error Pattern**: `RequestFailedException` with status 404
**Solution**:
- Verify the deployment name exists in Azure OpenAI Studio
- Check the deployment is in "Succeeded" state
- Ensure the model name matches exactly (case-sensitive)

### 4. **Rate Limiting**
**Error Pattern**: `RequestFailedException` with status 429
**Solution**:
- Implement exponential backoff (already handled by fallback)
- Check your quota limits in Azure portal
- Consider upgrading your pricing tier

### 5. **Regional/Quota Issues**
**Error Pattern**: `RequestFailedException` with status 403
**Solution**:
- Verify your subscription has access to Azure OpenAI
- Check if the model is available in your region
- Ensure you haven't exceeded quota limits

### 6. **Network Connectivity**
**Error Pattern**: `HttpRequestException` or `TaskCanceledException`
**Solution**:
- Check firewall/proxy settings
- Verify DNS resolution
- Test network connectivity to Azure

## Diagnostic Steps

### Step 1: Use the Diagnostic Function
Call the new diagnostic endpoint to check configuration:
```
GET /api/diagnostic
```

This will show:
- Environment variable status
- Azure OpenAI test result
- Configuration validation

### Step 2: Check Logs
Look for these specific log entries:
- `Azure OpenAI Configuration - Endpoint: {hasEndpoint}, ApiKey: {hasApiKey}, DeploymentName: {deploymentName}`
- `Azure OpenAI API request failed. Status: {status}, ErrorCode: {errorCode}`
- `Successfully rephrased instruction using Azure OpenAI`

### Step 3: Validate Configuration
Ensure these environment variables are set:
```json
{
  "AZURE_OPENAI_ENDPOINT": "https://your-resource.openai.azure.com/",
  "AZURE_OPENAI_API_KEY": "your-32-character-key",
  "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
}
```

### Step 4: Test Manually
Use Azure OpenAI Studio to test your deployment:
1. Go to Azure OpenAI Studio
2. Navigate to Chat playground
3. Select your deployment
4. Send a test message

## Error Code Reference

| Status Code | Meaning | Common Causes |
|-------------|---------|---------------|
| 400 | Bad Request | Invalid request format, missing parameters |
| 401 | Unauthorized | Invalid API key, expired key |
| 403 | Forbidden | No access to resource, quota exceeded |
| 404 | Not Found | Deployment doesn't exist, wrong endpoint |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Azure service issue |

## Configuration Examples

### Development (local.settings.json)
```json
{
  "Values": {
    "AZURE_OPENAI_ENDPOINT": "https://myopenai.openai.azure.com/",
    "AZURE_OPENAI_API_KEY": "abcd1234567890abcd1234567890abcd",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

### Production (Azure Function App Settings)
```bash
az functionapp config appsettings set \
  --name MyFunctionApp \
  --resource-group MyResourceGroup \
  --settings \
    AZURE_OPENAI_ENDPOINT="https://myopenai.openai.azure.com/" \
    AZURE_OPENAI_API_KEY="@Microsoft.KeyVault(SecretUri=https://myvault.vault.azure.net/secrets/openai-key/)" \
    DEPLOYMENT_OR_MODEL_NAME="gpt-4o-mini"
```

## Fallback Behavior

The service is designed to gracefully handle failures:
- **On Error**: Returns original instruction unchanged
- **Logs Error**: Detailed error information for debugging
- **Continues Operation**: Doesn't break the main functionality
- **Caches Success**: Reduces API calls for repeated instructions

## Performance Considerations

### Caching Strategy
- **Cache Duration**: 1 hour per instruction
- **Cache Key**: Instruction + random version (1-3)
- **Memory Usage**: Uses .NET MemoryCache with automatic cleanup

### Timeout Handling
- **Default Timeout**: 30 seconds (Azure OpenAI SDK default)
- **Retry Policy**: None (fails fast to avoid blocking)
- **Fallback**: Always returns original instruction on timeout

## Monitoring & Alerting

### Key Metrics to Monitor
1. **Success Rate**: Percentage of successful rephrasing attempts
2. **Response Time**: Average time for Azure OpenAI calls
3. **Error Rate**: Frequency of different error types
4. **Cache Hit Rate**: Effectiveness of caching strategy

### Recommended Alerts
- Error rate > 10% over 5 minutes
- Average response time > 10 seconds
- Any 401/403 errors (configuration issues)

## Getting Help

### Azure Support
- Check [Azure OpenAI Service Health](https://status.azure.com/)
- Review [Azure OpenAI Documentation](https://docs.microsoft.com/azure/cognitive-services/openai/)
- Contact Azure Support for service-specific issues

### Application Logs
Enable detailed logging for troubleshooting:
```json
{
  "Logging": {
    "LogLevel": {
      "GraderFunctionApp.Services.AIService": "Debug",
      "Default": "Information"
    }
  }
}
```

This will provide detailed information about:
- Configuration validation
- API request/response details
- Caching behavior
- Error specifics
