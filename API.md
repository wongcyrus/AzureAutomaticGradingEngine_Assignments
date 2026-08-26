# API Documentation

## Authentication and API Layers

Students call the Azure Static Web Apps routes shown below. Static Web Apps
requires Microsoft Entra authentication and injects the trusted student
principal. The API proxy derives the email from that principal and signs the
email, HTTP method, backend path/query, and a short-lived timestamp with a
CDKTN-managed HMAC key. Callers cannot select another student's email.

The underlying Function App endpoints use function-level authorization and are
for service-to-service or instructor diagnostics only. They require both a
Function key in the `x-functions-key` header and valid `x-grader-email`,
`x-grader-timestamp`, and `x-grader-signature` headers. A Function key alone
returns `401`.

Do not expose Function or proxy-signing keys in the browser. Static Web Apps
stores them only in server-side application settings. The proxy removes
`?code=` from configured backend URLs before forwarding and never logs
key-bearing URLs.

## Core Endpoints

### GET /api/game-task

Get next task assignment for a student.

**Parameters:**
- `npc` (required): NPC character name
- `game` (required): Game identifier (default: "azure-learning")

The authenticated student's email is supplied by the Static Web Apps proxy.

**Response:**
```json
{
  "status": "OK",
  "message": "Here's your next challenge...",
  "next_game_phrase": "TASK_ASSIGNED",
  "task_name": "AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist AzureProjectTestLib.ResourceGroupTest.Test02_ResourceGroupLocation",
  "score": 0,
  "completed_tasks": 0,
  "additional_data": {
    "instruction": "Create a resource group named 'projProd' in the Azure Brazil South region.",
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

Task assignment is serialized per student. If two different NPC requests
arrive together, only one can atomically create the active-task lock; the other
returns `BUSY_WITH_OTHER_NPC`. Duplicate requests to the winning NPC return the
same task.

### GET /api/grader

Submit work for grading.

**Parameters:**
- `npc` (required): NPC character name
- `game` (required): Game identifier

The authenticated student's email is supplied by the proxy and must not be
sent by the browser.

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

Completion atomically updates the NPC state and releases the student's active
task lock. Duplicate or delayed grading responses use ETags and cannot
overwrite a newer task.

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

View authenticated player identity, subscription, progress, active task, and
retained failure history.

**Parameters:**
- None. The authenticated student's email is supplied by the proxy.

**Response:**
```json
{
  "success": true,
  "data": {
    "email": "student@example.com",
    "subscriptionId": "00000000-0000-0000-0000-000000000000",
    "totalMarks": 50,
    "passedTasks": [
      {"name": "ResourceGroupTest", "mark": 10}
    ],
    "failedAttemptCount": 2,
    "failedAttempts": [
      {
        "testName": "StorageAccountTest",
        "taskName": "Create storage",
        "assignedByNpc": "Stella",
        "failedAt": "2026-08-23T03:00:00Z"
      }
    ],
    "activeTask": null,
    "lastActivity": "2026-08-23T03:00:00Z"
  }
}
```

### POST /api/pass-task

Reset the authenticated player's current game progress. The endpoint removes
all NPC game states, the active-task lock, score, and passed tests. It
preserves failed-attempt history, test-result blobs, subscription registration,
and Azure RBAC or Lighthouse access.

**Response:**
```json
{
  "success": true,
  "data": {
    "email": "student@example.com",
    "removedGameStates": 3,
    "removedPassedTests": 2,
    "preservedFailedAttempts": 5
  }
}
```

The Static Web Apps proxy supplies the authenticated identity and signs the
`POST`; callers cannot reset another player's data.

## Admin Endpoints

The teacher dashboard at `/admin.html` calls the operator-only Static Web Apps
routes below. Both the proxy and Function App require the authenticated email
to appear in `ADMIN_EMAILS`; the backend check remains authoritative. Valid
student identities receive `403` before any admin service is called.

| Static Web Apps route | Method | Operation |
| --- | --- | --- |
| `/api/teacher/status` | `GET` | Confirm the current operator session |
| `/api/teacher/cache-stats` | `GET` | Read generated-message cache statistics |
| `/api/teacher/cache-refresh` | `POST` | Regenerate cached messages |
| `/api/teacher/cache-reset` | `POST` | Reset cache hit counters |
| `/api/teacher/registration?email=...` | `GET` | Look up one exact registration |
| `/api/teacher/registration?email=...` | `DELETE` | Atomically release one exact registration |

The direct Function endpoints require a Function key plus valid
`x-grader-email`, `x-grader-timestamp`, and `x-grader-signature` headers. They
are for the server-side proxy and diagnostics, not browser use.

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

### POST /api/messages/refresh

Refresh the AI message cache. This admin operation requires the normal Function
key and an allowlisted signed operator identity. The timer-triggered refresh
also runs automatically each day at 02:00 UTC.

**Response:**
```json
{
  "success": true,
  "message": "Pre-generated messages have been successfully refreshed using optimized batching",
  "statistics": {
    "totalMessages": 451,
    "instructionMessages": 33,
    "npcMessages": 418
  },
  "timestamp": "2026-08-24T12:00:00Z"
}
```

### GET, DELETE /api/operator/subscription-registration

Look up or atomically release the two registration indexes for one exact
student email. Release succeeds only when the email and subscription index
rows form a consistent pair; missing or inconsistent data is never guessed or
partially deleted.

**Parameters:**
- `email` (required): Exact student sign-in email

`DELETE` preserves Azure access, the ownership tag, game progress, failed
attempts, reports, and test results.

## Error Responses

The student proxy preserves each backend's response contract. Game endpoints
use `GameResponse`, progress endpoints use the following envelope, and
registration returns short text or HTML messages:

```json
{
  "success": false,
  "error": "Error description",
  "details": "Additional error details (optional)"
}
```

**Common HTTP Status Codes:**
- `200`: Success
- `400`: Bad Request (missing parameters)
- `401`: Missing, expired, or invalid signed identity
- `403`: Grader access or initial ownership proof is missing
- `409`: Email or subscription is already registered
- `404`: Not Found (resource doesn't exist)
- `500`: Internal Server Error

## Rate Limiting

- NPC interactions: 1 hour cooldown between task assignments
- Grading: No limit (students can retry failed tasks)
- Admin endpoints: allowlisted operators only; no application-level rate limit

Signed assertions expire after five minutes and bind the identity to the exact
HTTP method and backend path/query. Replaying a signature for another student,
endpoint, or query fails authentication.

## Student Registration

### POST /api/registration

Registers the authenticated student's subscription after managed-identity
onboarding.

**Form field:**

- `subscriptionId`: Azure subscription GUID

The backend validates that:

- the grading identity can read the subscription;
- the assignment resource group exists;
- `projProd` has `GradingStudentEmail` equal to the authenticated email; and
- neither the email nor subscription has a conflicting registration.

Registration stores no Azure credential. It writes two entities: one with the
student email's SHA-256 hash in its row key and one keyed by subscription ID.
Both use partition `registrations` and are added in one Azure Table transaction.
Grading resolves the authenticated student with an exact email-index point
read; the subscription index prevents another student from claiming the same
subscription.

The same pair is idempotent. Partial or disagreeing indexes produce an explicit
integrity error rather than falling back to a tag or obsolete table.

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

The Static Web Apps proxies return the snake_case shape above. Direct Function
App responses use the equivalent camelCase .NET property names.

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
