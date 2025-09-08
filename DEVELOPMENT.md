# Development Guide

## Project Structure

```
├── GraderFunctionApp/          # Azure Functions backend
│   ├── Functions/              # HTTP trigger functions
│   ├── Services/               # Business logic services
│   ├── Models/                 # Data models
│   └── Interfaces/             # Service interfaces
├── azure-isekai/              # RPG Maker game frontend
│   ├── js/plugins/             # Game plugins
│   ├── data/                   # Game data files
│   └── img/                    # Game assets
├── AzureProjectTest/           # Unit test library
│   ├── Tests/                  # Test implementations
│   └── Models/                 # Test models
└── Infrastructure/             # CDK-TF deployment
    ├── stacks/                 # Infrastructure stacks
    └── constructs/             # Reusable constructs
```

## Development Setup

### Prerequisites

- Visual Studio Code or Visual Studio 2022
- .NET 8.0 SDK
- Node.js 18+
- Azure Functions Core Tools
- Azure CLI

### Local Development

1. **Clone and Setup**
   ```bash
   git clone <repository-url>
   cd AzureAutomaticGradingEngine_Assignments
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

Services are registered in `Program.cs`:
```csharp
builder.Services.AddScoped<IGameTaskService, GameTaskService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IAIService, AIService>();
```

### Service Layer Pattern

- **Controllers**: HTTP trigger functions handle requests
- **Services**: Business logic and data access
- **Models**: Data transfer objects and entities
- **Interfaces**: Service contracts for testability

### Message Caching Strategy

1. **Pre-generation**: AI messages generated in batches
2. **Hit Tracking**: Monitor cache effectiveness
3. **Fallback**: Live generation when cache misses

## Key Components

### Game State Management

```csharp
public class GameStateService : IGameStateService
{
    public async Task<GameState> GetGameStateAsync(string email, string game, string npc);
    public async Task<GameState> AssignTaskAsync(string email, string game, string npc, string taskName);
    public async Task<GameState> CompleteTaskAsync(string email, string game, string npc, string taskName, int reward);
}
```

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
    public async Task<string> RunUnitTestProcessAsync(ILogger logger, string credentials, string email, string filter);
}
```

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
   ```bash
   curl -X GET "https://function-app.azurewebsites.net/api/RefreshPreGeneratedMessages"
   ```

### Adding New Tests

1. **Create Test Class**
   ```csharp
   [TestClass]
   public class NewResourceTest : BaseTest
   {
       [TestMethod]
       public void Test01_ResourceExists()
       {
           // Test implementation
       }
   }
   ```

2. **Update Task Configuration**
   ```csharp
   new GameTaskData
   {
       Name = "NewResourceTest.Test01_ResourceExists",
       Instruction = "Create a new resource...",
       Filter = "NewResourceTest.Test01_ResourceExists",
       Reward = 10,
       Tests = new[] { "Test01_ResourceExists" }
   }
   ```

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

```csharp
[TestClass]
public class GameTaskServiceTests
{
    [TestMethod]
    public async Task GetNextTaskAsync_ReturnsCorrectTask()
    {
        // Arrange
        var service = new GameTaskService();
        
        // Act
        var result = await service.GetNextTaskAsync("test@example.com", "Stella", "azure-learning");
        
        // Assert
        Assert.IsNotNull(result);
    }
}
```

### Integration Tests

```csharp
[TestClass]
public class FunctionIntegrationTests
{
    [TestMethod]
    public async Task GameTaskFunction_ReturnsValidResponse()
    {
        // Test with real Azure resources
    }
}
```

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
2. **State Caching**: Cache game states in memory
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

- Function-level keys for API access
- Service principal for Azure resource access
- Input validation on all endpoints

### Data Protection

- Encrypt sensitive data at rest
- Use HTTPS for all communications
- Sanitize user inputs

### Access Control

- Minimal permissions for service principals
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

## Deployment Pipeline

### CI/CD with GitHub Actions

```yaml
name: Deploy Function App
on:
  push:
    branches: [main]
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Build and Deploy
        run: |
          dotnet publish -c Release
          func azure functionapp publish ${{ secrets.FUNCTION_APP_NAME }}
```

## Troubleshooting

### Common Issues

1. **Function timeouts**: Increase timeout in host.json
2. **Memory issues**: Use streaming for large responses
3. **Rate limiting**: Implement exponential backoff

### Debug Tools

- Azure Functions Core Tools for local debugging
- Application Insights for production monitoring
- Azure Storage Explorer for data inspection
