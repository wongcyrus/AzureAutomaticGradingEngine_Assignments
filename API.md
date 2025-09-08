# API Documentation

## Authentication

All endpoints require function-level authorization. Include the function key in requests:
```
?code=<function-key>
```

## Core Endpoints

### GET /api/game-task

Get next task assignment for a student.

**Parameters:**
- `email` (required): Student email
- `npc` (required): NPC character name
- `game` (required): Game identifier (default: "azure-learning")

**Response:**
```json
{
  "status": "OK",
  "message": "Here's your next challenge...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist",
  "score": 0,
  "completed_tasks": 0,
  "additional_data": {
    "instruction": "Create a resource group named 'projProd' in Hong Kong",
    "reward": 10,
    "tests": ["Test01_ResourceGroupExist", "Test02_ResourceGroupLocation"]
  }
}
```

**Status Codes:**
- `TASK_ASSIGNED`: New task assigned
- `BUSY_WITH_OTHER_NPC`: Student has active task with different NPC
- `NPC_COOLDOWN`: NPC recently assigned task (1-hour cooldown)
- `ALL_COMPLETED`: All tasks completed

### GET /api/grader

Submit work for grading.

**Parameters:**
- `email` (required): Student email
- `npc` (required): NPC character name
- `game` (required): Game identifier

**Response (Success):**
```json
{
  "status": "OK",
  "message": "Congratulations! Task completed!",
  "next_game_phrase": "READY_FOR_NEXT",
  "task_completed": true,
  "score": 10,
  "completed_tasks": 1,
  "easter_egg_url": "https://..."
}
```

**Response (Failure):**
```json
{
  "status": "OK",
  "message": "Task not completed yet. 1/2 tests passed.",
  "next_game_phrase": "TASK_ASSIGNED",
  "score": 0,
  "completed_tasks": 0,
  "additional_data": {
    "testResults": {"Test01": 1, "Test02": 0},
    "passedTests": 1,
    "totalTests": 2,
    "testResultXmlUrl": "https://..."
  }
}
```

### GET /api/pass-task

View completed tasks and scores.

**Parameters:**
- `email` (required): Student email

**Response:**
```json
{
  "status": "OK",
  "data": {
    "TotalMarks": 50,
    "PassedTasks": [
      {"Name": "ResourceGroupTest", "Mark": 10},
      {"Name": "StorageAccountTest", "Mark": 15}
    ]
  }
}
```

## Admin Endpoints

### GET /api/pregeneratedmessagestats

View message cache statistics.

**Response:**
```json
{
  "timestamp": "2025-01-08T00:00:00Z",
  "statistics": {
    "total": {
      "messages": 150,
      "hits": 45,
      "hitRate": 0.30,
      "unusedMessages": 105
    },
    "npc": {
      "messages": 120,
      "hits": 40,
      "hitRate": 0.33
    },
    "instructions": {
      "messages": 30,
      "hits": 5,
      "hitRate": 0.17
    }
  }
}
```

### POST /api/pregeneratedmessagestats/reset

Reset cache hit counts.

**Response:**
```json
{
  "message": "Hit counts reset successfully",
  "timestamp": "2025-01-08T00:00:00Z"
}
```

### GET /api/RefreshPreGeneratedMessages

Refresh AI message cache.

**Response:**
```json
{
  "message": "Pre-generated messages refreshed successfully",
  "timestamp": "2025-01-08T00:00:00Z",
  "statistics": {
    "instructionMessages": 30,
    "npcMessages": 120,
    "totalGenerated": 150
  }
}
```

### GET /api/test-result-xml/{blobName}

Retrieve test result XML with proper content-type.

**Parameters:**
- `blobName` (path): Name of the XML blob

**Response:**
- Content-Type: `text/xml`
- Body: XML test results

## Error Responses

All endpoints return errors in this format:
```json
{
  "status": "ERROR",
  "message": "Error description",
  "details": "Additional error details (optional)"
}
```

**Common HTTP Status Codes:**
- `200`: Success
- `400`: Bad Request (missing parameters)
- `404`: Not Found (resource doesn't exist)
- `500`: Internal Server Error

## Rate Limiting

- NPC interactions: 1 hour cooldown between task assignments
- Grading: No limit (students can retry failed tasks)
- Admin endpoints: No limit

## Data Models

### GameResponse
```typescript
interface GameResponse {
  status: "OK" | "ERROR";
  message: string;
  next_game_phrase?: string;
  task_name?: string;
  task_completed?: boolean;
  score?: number;
  completed_tasks?: number;
  easter_egg_url?: string;
  additional_data?: Record<string, any>;
}
```

### NPCCharacter
```typescript
interface NPCCharacter {
  Name: string;
  Age: number;
  Gender: string;
  Background: string;
}
```

### GameTaskData
```typescript
interface GameTaskData {
  Name: string;
  Instruction: string;
  Filter: string;
  Reward: number;
  TimeLimit: number;
  Tests: string[];
}
```
