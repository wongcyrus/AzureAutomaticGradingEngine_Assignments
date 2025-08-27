# Azure Isekai Game API System

This document describes the complete game API system that integrates the Azure Isekai RPG game with the Azure Automatic Grading Engine.

## System Overview

The game API system consists of several components that work together to provide an interactive learning experience:

1. **Frontend Game (azure-isekai)**: RPG Maker MV/MZ game with NPCs that students interact with
2. **API Layer (azure-isekai/api)**: Azure Functions that handle game requests
3. **Backend Services (GraderFunctionApp)**: Core grading and task management services
4. **Storage Layer**: Azure Tables for game state and progress tracking

## Game Flow

### Simplified 2-Phase System
The game uses a simple and intuitive flow that matches natural user interaction:

1. **First Chat with NPC** → Get a task assignment
2. **Second Chat with Same NPC** → Run grader and get results
   - If **Pass** → Task completed, ready for next task
   - If **Fail** → Same task remains active, try again

### Detailed Flow
1. **Task Assignment Phase**
   - Student talks to an NPC for the first time or after completing a task
   - `NpcK8sPluginCommand.js` calls `/api/game-task`
   - `game-task.js` forwards request to `GameTaskFunction.cs`
   - System assigns next available uncompleted task
   - Response includes task details, instructions, and current progress

2. **Work Phase**
   - Student works on Azure infrastructure setup outside the game
   - Task remains active in the system

3. **Grading Phase**
   - Student returns to the same NPC for grading
   - `NpcK8sPluginCommand.js` calls `/api/grader`
   - `grader.js` forwards request to `GraderFunction.cs`
   - System runs unit tests against student's Azure setup
   - Results determine next action:
     - **Success**: Task marked complete, ready for next task
     - **Failure**: Task remains active, student can retry

4. **Repeat Cycle**
   - Process repeats until all tasks are completed

## API Endpoints

### `/api/game-task`
**Purpose**: Get a new task or check current task status
**Parameters**:
- `game`: Game identifier
- `npc`: NPC name
- `email`: Student email (from authentication)

**Response**:
```json
{
  "status": "OK",
  "message": "Task description and instructions",
  "next_game_phrase": "READY",
  "task_name": "Create Virtual Networks",
  "score": 150,
  "completed_tasks": 3,
  "additional_data": {
    "instruction": "Detailed task instructions",
    "timeLimit": 60,
    "reward": 50,
    "tests": ["TestVirtualNetwork", "TestSubnets"]
  }
}
```

### `/api/grader`
**Purpose**: Grade current active task and update game state
**Parameters**:
- `game`: Game identifier
- `npc`: NPC name
- `email`: Student email (from authentication)

**Note**: Azure credentials are automatically retrieved from the Credential table in Azure Storage. The system automatically runs grading for the student's currently active task.

**Response**:
```json
{
  "status": "OK",
  "message": "Grading results and feedback",
  "next_game_phrase": "READY_FOR_NEXT", // or "TASK_ASSIGNED" if failed
  "task_completed": true, // or false if failed
  "score": 200,
  "completed_tasks": 4,
  "report_url": "/api/report?email=student@example.com&task=TaskName",
  "easter_egg_url": "https://example.com/congratulations"
}
```

## Game States

The system uses a simple 2-state approach that matches the natural user flow:

1. **TASK_ASSIGNED**: Student has an active task to work on
2. **READY_FOR_NEXT**: Student has completed current task and is ready for the next one
3. **ALL_COMPLETED**: Student has completed all available tasks

### State Transitions
- **Initial State** → `READY_FOR_NEXT` (ready to get first task)
- **READY_FOR_NEXT** → `TASK_ASSIGNED` (when new task is assigned)
- **TASK_ASSIGNED** → `READY_FOR_NEXT` (when task is completed successfully)
- **TASK_ASSIGNED** → `TASK_ASSIGNED` (when task fails, remains active)
- **READY_FOR_NEXT** → `ALL_COMPLETED` (when no more tasks available)

## Database Schema

### GameStates Table
Tracks individual game sessions and progress:

| Field | Type | Description |
|-------|------|-------------|
| PartitionKey | string | Student email |
| RowKey | string | Game-NPC combination |
| CurrentPhase | string | Current game state (TASK_ASSIGNED, READY_FOR_NEXT, ALL_COMPLETED) |
| CurrentTaskName | string | Active task name (empty if no active task) |
| CurrentTaskFilter | string | Test filter for grading |
| CurrentTaskReward | int | Points for current task |
| LastMessage | string | Last message shown to student |
| TotalScore | int | Student's total score |
| CompletedTasks | int | Number of completed tasks |
| CompletedTasksList | string | JSON array of completed task names |
| HasActiveTask | bool | Whether student has an active task |

## Credential Management

### Credential Storage
Azure credentials are securely stored in the `Credential` table in Azure Storage. Each student must register their Azure service principal credentials before they can use the grading system.

### Credential Table Schema
| Field | Type | Description |
|-------|------|-------------|
| PartitionKey | string | Student email |
| RowKey | string | Student email (same as PartitionKey) |
| AppId | string | Azure service principal application ID |
| DisplayName | string | Service principal display name |
| Password | string | Service principal password/secret |
| Tenant | string | Azure tenant ID |
| SubscriptionId | string | Azure subscription ID |

### Registration Process
1. Students use the `StudentRegistrationFunction` to register their Azure credentials
2. The system validates the credentials and subscription access
3. Credentials are securely stored in the Credential table
4. During grading, credentials are automatically retrieved and used for testing

### Security Features
- Credentials are encrypted at rest in Azure Storage
- Access is restricted to authenticated users only
- Each student can only access their own credentials
- Service principal permissions are limited to Reader role at subscription level

## Key Components

### Frontend (NpcK8sPluginCommand.js)
- Manages game state per NPC
- Handles API calls and response processing
- Provides visual feedback to students
- Supports debugging functions (`resetGameState`, `checkGameState`)

### API Layer (game-task.js, grader.js)
- Handles authentication and request validation
- Forwards requests to backend services
- Transforms responses for frontend consumption
- Provides error handling and logging

### Backend Services
- **GameTaskFunction**: Manages task assignment and progress
- **GraderFunction**: Handles test execution and grading
- **GameStateService**: Manages persistent game state
- **StorageService**: Handles data persistence

### Infrastructure (main.ts)
- Provisions Azure resources (Functions, Storage, Tables)
- Configures authentication and networking
- Sets up CI/CD pipeline integration

## Development and Debugging

### Local Development
1. Set up environment variables in `.env` file
2. Deploy infrastructure: `cdktf deploy --auto-approve`
3. Build and publish test project: `dotnet publish -r win-x64 -c Release`
4. Upload tests to Azure File Share
5. Test API endpoints using browser or Postman

### Debugging Game State
Use browser console commands:
```javascript
// Check current game state for all NPCs
checkGameState();

// Check state for specific NPC
checkGameState('TrainerNPC');

// Reset game state (for testing)
resetGameState('TrainerNPC');
```

### Monitoring
- Application Insights tracks all API calls and errors
- Azure Storage Explorer can be used to view game state data
- Function logs provide detailed execution information

## Security Considerations

- Authentication handled by Azure Static Web Apps
- Credentials passed securely through HTTPS
- Game state isolated per student
- Test execution runs in sandboxed environment

## Future Enhancements

1. **Leaderboards**: Track top performers across all students
2. **Achievements**: Unlock special rewards for completing challenges
3. **Multiplayer**: Allow students to collaborate on tasks
4. **Advanced Analytics**: Track learning patterns and difficulty areas
5. **Custom Tasks**: Allow instructors to create new challenges
