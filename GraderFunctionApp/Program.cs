using GraderFunctionApp.Functions;
using GraderFunctionApp.Services;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((hostContext, config) =>
    {
        config.AddJsonFile("host.json", optional: true);
        config.AddJsonFile("local.settings.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configuration Options
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<TestRunnerOptions>(configuration.GetSection(TestRunnerOptions.SectionName));

        // Register StorageService with connection string
        services.AddSingleton<IStorageService>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<StorageService>();
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string not found");
            var storageOptions = Microsoft.Extensions.Options.Options.Create(new StorageOptions());
            return new StorageService(connectionString, logger, storageOptions);
        });

        // Register GameStateService with TableServiceClient
        services.AddSingleton<IGameStateService>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<GameStateService>();
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string not found");
            var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connectionString);
            return new GameStateService(tableServiceClient, logger);
        });

        // Register TableServiceClient for shared use
        services.AddSingleton<Azure.Data.Tables.TableServiceClient>(provider =>
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string not found");
            return new Azure.Data.Tables.TableServiceClient(connectionString);
        });

        // Register core services without circular dependencies
        services.AddSingleton<IGameTaskService, GameTaskService>();
        
        // Register AIService first without PreGeneratedMessageService dependency
        services.AddSingleton<IAIService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<AIService>>();
            return new AIService(logger, null); // Start without pre-generated service
        });
        
        // Register PreGeneratedMessageService with dependencies
        services.AddSingleton<IPreGeneratedMessageService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<PreGeneratedMessageService>>();
            var tableServiceClient = provider.GetRequiredService<Azure.Data.Tables.TableServiceClient>();
            var storageOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>();
            var aiService = provider.GetRequiredService<IAIService>();
            var gameTaskService = provider.GetRequiredService<IGameTaskService>();
            return new PreGeneratedMessageService(logger, tableServiceClient, storageOptions, aiService, gameTaskService);
        });
        
        services.AddSingleton<IUnifiedMessageService, UnifiedMessageService>();
        services.AddSingleton<ITestResultParser, TestResultParser>();
        services.AddSingleton<ITestRunner, TestRunner>();

        // Register Functions (for backward compatibility)
        services.AddSingleton<GameTaskFunction>();
    })
    .ConfigureLogging((hostingContext, logging) =>
    {
        logging.AddApplicationInsights(console =>
        {
            console.IncludeScopes = true;
        });
        logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));

        // Remove default Application Insights filter
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            LoggerFilterRule? defaultRule = options?.Rules?.FirstOrDefault(rule => rule.ProviderName
                == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options?.Rules.Remove(defaultRule);
            }
        });
    })
    .Build();

host.Run();
