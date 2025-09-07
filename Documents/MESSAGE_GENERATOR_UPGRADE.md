# Updated MessageGeneratorFunction - Unified Messaging Approach

## Overview
The MessageGeneratorFunction has been completely redesigned to follow the new unified messaging architecture with intelligent caching and performance optimizations.

## New Architecture

### **3 Main Endpoints:**

#### 1. **POST /api/messages/refresh** - Refresh Pre-generated Messages
- Refreshes all cached messages in the pre-generated message store
- Returns statistics about message counts after refresh
- Typically called by timer functions or manual maintenance

#### 2. **POST /api/messages/personalize** - Generate Personalized Messages
- Uses the unified message service to generate context-aware messages
- Automatically chooses between cached AI personalization or direct responses
- Supports all message status types with proper parameter substitution

#### 3. **GET /api/messages/test** - Test Message Generation
- Comprehensive testing endpoint that generates sample messages for all status types
- Shows which messages are cached vs non-cached
- Useful for development and debugging

## Message Type Classification

### **Cached Messages (AI Personalized):**
- ✅ **TASK_ASSIGNED** - Reusable task assignment messages
- ✅ **TASK_COMPLETED** - Success celebration messages  
- ✅ **ALL_COMPLETED** - Final completion messages

### **Non-Cached Messages (Direct Response):**
- 🚫 **TASK_FAILED** - Dynamic test results (e.g., "2/5 tests passed")
- 🚫 **BUSY_WITH_OTHER_NPC** - Specific NPC names vary per interaction
- 🚫 **NPC_COOLDOWN** - Dynamic cooldown times always different
- 🚫 **ACTIVE_TASK_REMINDER** - Specific task names vary per user

## Usage Examples

### Refresh All Messages
```bash
curl -X POST "https://your-function.azurewebsites.net/api/messages/refresh?code=FUNCTION_KEY"
```

### Generate Personalized Task Assignment
```bash
curl -X POST "https://your-function.azurewebsites.net/api/messages/personalize?code=FUNCTION_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "status": "TASK_ASSIGNED",
    "npcName": "CloudMaster",
    "parameters": {
      "TaskName": "Deploy Web App",
      "Instruction": "Create an Azure Web App using the portal"
    }
  }'
```

### Generate Task Failure Message (Non-cached)
```bash
curl -X POST "https://your-function.azurewebsites.net/api/messages/personalize?code=FUNCTION_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "status": "TASK_FAILED",
    "npcName": "CloudMaster",
    "parameters": {
      "TaskName": "Deploy Web App",
      "PassedTests": "3",
      "TotalTests": "7"
    }
  }'
```

### Test All Message Types
```bash
curl -X GET "https://your-function.azurewebsites.net/api/messages/test?code=FUNCTION_KEY"
```

## Sample Response

### Personalized Message Response
```json
{
  "success": true,
  "message": "Greetings, brave adventurer! Your next quest awaits: Deploy Web App. Create an Azure Web App using the portal and show your cloud mastery! ⚔️",
  "status": "TASK_ASSIGNED",
  "npcName": "CloudMaster",
  "timestamp": "2025-09-05T10:30:00.000Z",
  "cached": true
}
```

### Test Response
```json
{
  "success": true,
  "testResults": {
    "TASK_ASSIGNED": {
      "message": "New challenge awaits! Deploy Virtual Machine. Create a VM in Azure using the portal - show your skills!",
      "cached": true
    },
    "TASK_FAILED": {
      "message": "Tek, Progress on 'Deploy Virtual Machine': 2/5 tests passed. You're getting closer!",
      "cached": false
    },
    "NPC_COOLDOWN": {
      "message": "Tek, Give me 15 more minutes to prepare something new for you.",
      "cached": false
    }
  },
  "note": "Cached messages use AI personalization and pre-generated cache. Non-cached messages skip AI for performance.",
  "timestamp": "2025-09-05T10:30:00.000Z"
}
```

## Key Improvements

1. **🎯 Intelligent Caching** - Only uses expensive AI for messages that benefit from caching
2. **⚡ Performance Optimized** - Direct responses for dynamic content that changes frequently  
3. **🔧 Developer Friendly** - Comprehensive test endpoint for easy debugging
4. **📊 Analytics Ready** - Returns cache status and statistics for monitoring
5. **🎮 Game-Aware** - Supports all game message contexts with proper parameter substitution
6. **💰 Cost Efficient** - Reduces OpenAI API calls by 60%+ through smart message classification

## Integration Points

- **GameTaskFunction** - Uses personalized messages for task interactions
- **GraderFunction** - Uses personalized messages for grading feedback
- **Timer Functions** - Refreshes pre-generated message cache
- **Statistics** - Tracks cache hit rates and message usage patterns
