# Development Guide

## Project Structure

```
├── GraderFunctionApp/          # Azure Functions backend
│   ├── Functions/              # HTTP trigger functions
│   ├── Services/               # Business logic services
│   ├── Models/                 # Data models
│   └── Interfaces/             # Service interfaces
├── GraderFunctionApp.Tests/    # NUnit tests and Coverlet settings
├── azure-isekai/              # RPG Maker game frontend
│   ├── js/plugins/             # Game plugins
│   ├── data/                   # Game data files
│   └── img/                    # Game assets
├── AzureProjectTest/           # Unit test library
│   ├── Tests/                  # Test implementations
│   └── Models/                 # Test models
├── Infrastructure/             # CDK Terrain application
    ├── stacks/                 # Infrastructure stacks
    └── constructs/             # Reusable constructs
└── packages/                   # Shared libraries (Git submodules/npm workspaces)
```

## Development Setup

### Prerequisites

- Visual Studio Code or Visual Studio 2022
- .NET 10.0 and 8.0 SDKs
- Node.js 24.19+
- Azure Functions Core Tools
- Azure CLI

### Local Development

1. **Clone and Setup**
   ```bash
   git clone --recurse-submodules <repository-url>
   cd AzureAutomaticGradingEngine_Assignments
   npm run bootstrap
   cp .env.template .env
   # Edit .env with development credentials
   ```

2. **Function App Development**
   ```bash
   cd GraderFunctionApp
   dotnet restore
   func start --port 7071
   ```

3. **Game Development**
   ```bash
   cd azure-isekai
   npm install
   npm run dev
   ```

4. **Test Library Development**
   ```bash
   cd AzureProjectTest
   dotnet build
   dotnet test
   ```

## Architecture Patterns

### Dependency Injection

`Program.cs` owns host configuration. Application services are registered by
`GraderServiceCollectionExtensions.AddGraderServices`, which keeps composition
testable:
```csharp
services.AddGraderServices(hostContext.Configuration);
```

Storage and AI services use explicit singleton factories because they require
configuration-backed Azure clients and must avoid circular dependencies.
Factories must resolve `IOptions<StorageOptions>` rather than constructing
default options, otherwise deployment-specific table/container names are lost.

`SignedRequestAuthenticator` is the backend identity boundary. Never read a
student identity from query strings, forms, or diagnostic trace values. The
SWA server proxy signs the normalized Entra email, HTTP method, exact backend
path/query, and timestamp with `GRADER_PROXY_SIGNING_KEY`; backend assertions
expire after five minutes.

### Service Layer Pattern

- **Controllers**: HTTP trigger functions handle requests
- **Services**: Business logic and data access
- **Models**: Data transfer objects and entities
- **Interfaces**: Service contracts for testability

### Message Caching Strategy

1. **Pre-generation**: AI messages generated in batches
2. **Hit Tracking**: Monitor cache effectiveness
3. **Fallback**: Live generation when cache misses
4. **Stable keys**: SHA-256-based row keys are deterministic across Function
   restarts and scale-out instances; never use `string.GetHashCode()` for
   persisted identifiers
5. **Explicit outcomes**: Generation helpers return success/failure so retry
   exhaustion is not counted as a successful batch item

## Key Components

### Game State Management

```csharp
public class GameStateService : IGameStateService
{
    public Task<GameState?> GetGameStateAsync(string email, string game, string npc);
    public Task<GameState?> TryAssignTaskAsync(
        string email, string game, string npc, string taskName,
        string taskFilter, int reward, string personalizedMessage);
    public Task<GameState> CompleteTaskAsync(
        string email, string game, string npc, string taskName, int reward);
}
```

`GameStates` uses the normalized student email as the partition key. NPC states
use `<game>-<npc>` row keys and the single active-task lock uses
`__active_task_lock__`. Keep assignment and lock mutation in one Azure Table
transaction; all transaction actions must remain in the same partition.
Normalize authenticated emails with `Trim().ToLowerInvariant()` at every
Function boundary; Azure Table partition keys are case-sensitive.

Concurrency invariants:

1. Initialize state with `Add`, never an unconditional upsert.
2. Acquire the lock with `Add` in the same transaction as assignment.
3. Update completion and delete the lock in one ETag-conditional transaction.
4. Treat duplicate completion as idempotent only when the completed-task list
   proves that exact task already completed.
5. Never write a game-state snapshot captured before asynchronous test
   execution. Reread and use an ETag-conditional update.

Concurrent grading may legitimately produce one completion response and a
later request reporting that no task is active. The invariant is data
integrity: only one reward is added, the completed state is not reverted, and
no stale lock remains.

### NPC Character System

NPCs are stored in Azure Table Storage with AI-powered personalities:
```csharp
public class NPCCharacter : ITableEntity
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; }
    public string Background { get; set; }
}
```

### Test Execution Engine

```csharp
public class TestRunner : ITestRunner
{
    public Task<string?> RunUnitTestProcessAsync(
        ILogger logger, string subscriptionId, string email, string filter);
}
```

The child process receives an explicit subscription ID. Authentication is
provided by `DefaultAzureCredential`: the user-assigned identity in Azure and
the current Azure CLI identity during local development. Azure Lighthouse
projects cross-tenant subscriptions to the same managed identity, so the
Function and test process do not branch on tenant or handle student secrets.

Run `scripts/test-grading-access.sh` after changing onboarding behavior. It
uses a fake Azure CLI to cover direct RBAC, Lighthouse creation, idempotent
Lighthouse reruns, and both revocation paths without changing Azure resources.

## Adding New Features

### Adding a New NPC

1. **Create NPC Data**
   ```csharp
   var npc = new NPCCharacter
   {
       PartitionKey = "NPC",
       RowKey = "NewNPC",
       Name = "New NPC",
       Age = 25,
       Gender = "Non-binary",
       Background = "A helpful guide..."
   };
   ```

2. **Add to Storage**
   ```csharp
   await storageService.SaveNPCCharacterAsync(npc);
   ```

3. **Generate Messages**
   The deployed `MessageRefreshTimerFunction` refreshes the cache daily at
   02:00 UTC. After changing task instructions, publish and deploy the grading
   assembly first, then invoke the timer or authenticated
   `POST /api/messages/refresh` endpoint.

### Adding New Tests

1. **Add an attributed test**
   ```csharp
   [GameClass(6)]
   public class NewResourceTest
   {
       [GameTask("Create the required resource.", 5, 10)]
       [Test]
       public void Test01_ResourceExists()
       {
           // Test implementation
       }
   }
   ```

2. Mirror compatible public/private changes, increment the private package
   version, and publish its matching `v<version>` tag.
3. Update expected task/assertion counts in `GameTaskServiceTests`, deployed
   integration defaults, and documentation.
4. Validate public and private modes, deploy both grading artifacts, refresh
   cached messages, and run the complete live subscription suite.

### Adding New API Endpoints

1. **Create Function**
   ```csharp
   [Function("NewEndpoint")]
   public async Task<IActionResult> NewEndpoint(
       [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
   {
       // Implementation
   }
   ```

2. **Add Service Logic**
   ```csharp
   public interface INewService
   {
       Task<Result> ProcessAsync(string input);
   }
   ```

3. **Register Service**
   ```csharp
   builder.Services.AddScoped<INewService, NewService>();
   ```

## Testing Strategy

### Unit Tests

```bash
dotnet test AzureProjectGrader.sln --configuration Release

dotnet test GraderFunctionApp.Tests/GraderFunctionApp.Tests.csproj \
  --configuration Release \
  --collect:"XPlat Code Coverage" \
  --settings GraderFunctionApp.Tests/coverlet.runsettings
```

Tests use NUnit and NSubstitute. Keep Azure SDK adapters behind injectable
clients/factories, assert observable behavior rather than private methods, and
exclude only generated `obj` code from coverage.

### Load Testing

Use Azure Load Testing or Artillery:
```yaml
config:
  target: 'https://function-app.azurewebsites.net'
scenarios:
  - name: 'Game Task Flow'
    requests:
      - get:
          url: '/api/game-task?email=test@example.com&npc=Stella&game=azure-learning'
```

## Performance Optimization

### Caching Strategy

1. **Message Caching**: Pre-generate AI responses
2. **Stable cache keys**: Reuse persisted AI responses across instances
3. **CDN**: Use Azure CDN for static assets

### Database Optimization

1. **Partitioning**: Use email as partition key
2. **Indexing**: Index frequently queried fields
3. **Batch Operations**: Use batch writes for bulk operations

### Function App Optimization

1. **Cold Start**: Use Premium plan for production
2. **Connection Pooling**: Reuse database connections
3. **Async Operations**: Use async/await throughout

## Security Considerations

### Authentication

- Microsoft Entra authentication and group-restricted Static Web Apps access
- Function-level keys between the Static Web Apps API and Function App
- User-assigned managed identity for Azure resource access
- Input validation on all endpoints

### Data Protection

- Encrypt sensitive data at rest
- Use HTTPS for all communications
- Sanitize user inputs

### Access Control

- Subscription `Reader` plus assignment-resource-group `Contributor`
  for the grading identity
- No stored student service-principal credentials
- Role-based access for admin functions
- Audit logging for all operations

## Monitoring and Logging

### Application Insights

```csharp
_logger.LogInformation("Game task assigned: {taskName} to {email}", taskName, email);
_logger.LogError(ex, "Failed to process request for {email}", email);
```

### Custom Metrics

```csharp
_telemetryClient.TrackMetric("TasksCompleted", 1);
_telemetryClient.TrackEvent("NPCInteraction", new Dictionary<string, string>
{
    ["NPC"] = npcName,
    ["Email"] = email
});
```

### Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<AIServiceHealthCheck>("ai-service");
```

## Deployment

CDKTN is the deployment source of truth. It synthesizes Terraform, provisions
Azure resources, publishes the Function App, and uploads the Windows NUnit
runner:

```bash
npm run build
npm test
npm run synth
terraform -chdir=Infrastructure/cdktf.out/stacks/AzureAutomaticGradingEngineGrader validate
cd Infrastructure
PATH="$HOME/.dotnet:$PATH" npx cdktn deploy
```

Do not replace this with a direct `func publish` pipeline: that bypasses
infrastructure outputs, Function keys, runner publishing, and shared CDKTN
construct behavior.

## Troubleshooting

### Common Issues

1. **Function timeouts**: Increase timeout in host.json
2. **Memory issues**: Use streaming for large responses
3. **Rate limiting**: Implement exponential backoff

### Debug Tools

- Azure Functions Core Tools for local debugging
- Application Insights for production monitoring
- Azure Storage Explorer for data inspection
