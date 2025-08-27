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
        services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName));
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

        // Register other services
        services.AddSingleton<IAIService, AIService>();
        services.AddSingleton<ITestResultParser, TestResultParser>();
        services.AddSingleton<IGameTaskService, GameTaskService>();
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
