# Azure OpenAI Endpoint Format Fix

## Issue Identified
**Error**: `HTTP 404 (404) Resource not found`
**Root Cause**: Using old Cognitive Services endpoint format instead of new Azure OpenAI format

## The Problem
Your current endpoint: `https://hkiitazureai.cognitiveservices.azure.com/`
Should be: `https://hkiitazureai.openai.azure.com/`

## Quick Fix

### Option 1: Update Configuration (Recommended)
Update your `local.settings.json`:

```json
{
  "Values": {
    "AZURE_OPENAI_ENDPOINT": "https://hkiitazureai.openai.azure.com/",
    "AZURE_OPENAI_API_KEY": "your-api-key",
    "DEPLOYMENT_OR_MODEL_NAME": "gpt-4o-mini"
  }
}
```

### Option 2: Get Correct Endpoint from Azure Portal
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your Azure OpenAI resource (`hkiitazureai`)
3. Go to **Keys and Endpoint**
4. Copy the **Endpoint** value (should be the new format)

## Endpoint Format Evolution

### Old Format (Deprecated)
```
https://[resource-name].cognitiveservices.azure.com/
```

### New Format (Current)
```
https://[resource-name].openai.azure.com/
```

## Automatic Correction
The updated AIService now includes automatic endpoint correction:
- Detects old format endpoints
- Automatically converts to new format
- Logs the correction for visibility

## Verification Steps

### 1. Update Configuration
Change your endpoint from:
```
https://hkiitazureai.cognitiveservices.azure.com/
```
To:
```
https://hkiitazureai.openai.azure.com/
```

### 2. Test with Diagnostic Function
```bash
curl -X GET "http://localhost:7071/api/diagnostic"
```

### 3. Expected Success Response
```json
{
  "tests": {
    "AzureOpenAI_Direct": {
      "status": "Success",
      "response": "Hello, this is a test!"
    }
  }
}
```

## Why This Happens
- Azure OpenAI service evolved from Cognitive Services
- Older resources may still show the old endpoint format
- The new Azure OpenAI SDK requires the new format
- Some documentation may still reference the old format

## Additional Checks

### Verify Your Resource Type
1. In Azure Portal, check your resource type
2. Should be "Azure OpenAI" not "Cognitive Services"
3. If it's Cognitive Services, you may need to create a new Azure OpenAI resource

### Check API Version
The error shows API version `2025-01-01-preview` which is very recent. Ensure your:
- Azure OpenAI resource supports this version
- Deployment is using a compatible model version

## Troubleshooting

### Still Getting 404?
1. **Check Deployment Name**: Ensure `gpt-4o-mini` deployment exists
2. **Check Region**: Ensure your resource region supports the model
3. **Check Status**: Verify deployment status is "Succeeded" in Azure OpenAI Studio

### Check Deployment in Azure OpenAI Studio
1. Go to [oai.azure.com](https://oai.azure.com)
2. Select your resource
3. Navigate to **Deployments**
4. Verify `gpt-4o-mini` exists and is active

## Prevention
- Always use the endpoint from Azure Portal > Keys and Endpoint
- Bookmark the correct format pattern: `https://[name].openai.azure.com/`
- Update any documentation or templates with the new format

## Next Steps
1. Update your endpoint configuration
2. Restart your function app
3. Test with the diagnostic endpoint
4. Monitor logs for successful AI rephrasing
