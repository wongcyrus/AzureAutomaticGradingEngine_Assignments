# Pre-Generated Messages System

## Overview

This system implements a cost-effective and performance-optimized solution for OpenAI message generation by pre-generating common messages and storing them in Azure Table Storage. This reduces both latency and costs associated with frequent OpenAI API calls.

## Features

### 1. Pre-Generated Message Storage
- **Table**: `PreGeneratedMessages` in Azure Table Storage
- **Model**: `PreGeneratedMessage.cs`
- **Partition Keys**: 
  - `instruction` - for game instruction messages
  - `npc` - for NPC personalized messages

### 2. Message Types Supported
- **Instruction Messages**: Gamified Azure task instructions
- **NPC Messages**: Personalized messages based on NPC characteristics (age, gender, background)

### 3. Automated Daily Refresh
- **Timer Function**: `MessageRefreshTimerFunction.cs`
- **Schedule**: Daily at 2:00 AM UTC (`0 0 2 * * *`)
- **Purpose**: Refresh and generate new common messages

### 4. Manual Message Generation
- **HTTP Function**: `MessageGeneratorFunction.cs`
- **Endpoint**: `POST /api/generate-messages`
- **Purpose**: On-demand generation for initial setup or testing

## How It Works

### Message Retrieval Flow
1. **Check Pre-generated**: First attempts to retrieve from `PreGeneratedMessages` table
2. **Cache Check**: Falls back to in-memory cache if available
3. **Live Generation**: Only calls OpenAI API if no pre-generated message exists
4. **Fallback**: Returns original message if all methods fail

### Message Generation
- **Hash-based Keys**: Uses SHA256 hash of original message (+ NPC characteristics for NPC messages)
- **Deduplication**: Prevents duplicate message generation
- **Error Handling**: Graceful fallback to live generation on errors

## Configuration

### Storage Options
Add to `StorageOptions.cs`:
```csharp
public string PreGeneratedMessageTableName { get; set; } = "PreGeneratedMessages";
```

### Infrastructure
The `PreGeneratedMessages` table is automatically created through the Infrastructure as Code setup in:
- `Infrastructure/constructs/GradingEngineStorageConstruct.ts`

## Services

### IPreGeneratedMessageService
Main interface for pre-generated message operations:
- `GetPreGeneratedInstructionAsync()` - Retrieve instruction messages
- `GetPreGeneratedNPCMessageAsync()` - Retrieve NPC messages  
- `RefreshAllPreGeneratedMessagesAsync()` - Refresh all messages
- `GenerateCommonInstructionMessagesAsync()` - Generate common instructions
- `GenerateCommonNPCMessagesAsync()` - Generate common NPC messages

### Updated AIService
Enhanced to use pre-generated messages:
- Checks pre-generated messages first
- Falls back to live OpenAI generation
- Maintains existing caching behavior

## Pre-Generated Content

### Common Instructions (15 messages)
- Resource Group creation
- Storage Account deployment
- Function App setup
- Key Vault configuration
- Virtual Network creation
- SQL Database deployment
- Application Insights setup
- Container Registry creation
- Service Bus configuration
- Cosmos DB deployment
- App Service Plan creation
- Log Analytics Workspace
- API Management service
- Redis Cache instance
- Event Hub namespace

### Common NPC Messages (10 base messages × 5 NPC profiles = 50 messages)
**Base Messages:**
- Welcome messages
- Completion congratulations
- Encouragement messages
- Progress acknowledgments
- Challenge introductions

**NPC Profiles:**
- Female Cloud Architect (25)
- Male DevOps Engineer (30) 
- Female Solution Architect (28)
- Male Senior Developer (35)
- Female Cloud Engineer (26)

## Performance Benefits

### Cost Reduction
- **Pre-generated Messages**: $0 per retrieval
- **Live OpenAI Calls**: ~$0.001-0.003 per message
- **Estimated Savings**: 70-80% reduction in OpenAI costs

### Latency Improvement  
- **Pre-generated Retrieval**: ~10-50ms
- **Live OpenAI Generation**: ~1000-3000ms
- **Performance Gain**: 20-30x faster response times

### Scalability
- **Cache Hit Rate**: Expected 60-80% for common messages
- **Storage Cost**: Negligible (~$0.10/month for thousands of messages)
- **No Rate Limiting**: Eliminates OpenAI rate limit concerns

## Deployment

### 1. Infrastructure Update
The table is automatically created when deploying the infrastructure:
```bash
cd Infrastructure
npm run deploy
```

### 2. Initial Message Generation
After deployment, trigger initial message generation:
```bash
curl -X POST "https://your-function-app.azurewebsites.net/api/generate-messages?code=YOUR_FUNCTION_KEY"
```

### 3. Verify Timer Function
The timer function will run automatically daily at 2 AM UTC. Check logs to verify execution.

## Monitoring

### Key Metrics to Monitor
- Pre-generated message hit rate
- OpenAI API call frequency
- Timer function execution success
- Message generation errors

### Log Messages
- `"Using pre-generated instruction message"` - Successful cache hit
- `"No pre-generated message found, falling back to live generation"` - Cache miss
- `"Generated {count} common messages"` - Successful batch generation

## Maintenance

### Adding New Common Messages
Update the arrays in `PreGeneratedMessageService.cs`:
- `commonInstructions` - for new instruction templates
- `commonMessages` - for new NPC message templates
- `npcProfiles` - for new NPC personality profiles

### Message Refresh
Messages are automatically refreshed daily, but can be manually triggered:
- Use the HTTP endpoint for immediate refresh
- Timer function ensures regular updates without manual intervention

## Troubleshooting

### Common Issues
1. **Missing Table**: Ensure infrastructure is deployed correctly
2. **No Pre-generated Messages**: Trigger manual generation first
3. **Timer Not Running**: Check Function App configuration and logs
4. **High OpenAI Costs**: Monitor hit rates and add more common messages

### Debug Commands
```bash
# Check table exists
az storage table list --account-name YOUR_STORAGE_ACCOUNT

# View function logs  
az functionapp logs tail --name YOUR_FUNCTION_APP --resource-group YOUR_RG

# Test message generation
curl -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/generate-messages?code=YOUR_KEY"
```
