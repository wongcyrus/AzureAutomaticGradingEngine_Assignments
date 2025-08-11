using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(static services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        // Register your services here
        _ = services.AddSingleton<GraderFunctionApp.StorageService>(static provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<GraderFunctionApp.StorageService>();
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string not found");
            return new GraderFunctionApp.StorageService(connectionString, logger);
        });
    })
    .ConfigureAppConfiguration(static (hostContext, config) => //This is for facilitating the logging functionality.
    {
        config.AddJsonFile("host.json", optional: true);
    })
    .ConfigureLogging(static (hostingContext, logging) =>
    {
        logging.AddApplicationInsights(static console =>
        {
            console.IncludeScopes = true;
        });
        logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
    }).ConfigureLogging(static logging => //This is for facilitating the logging functionality in Application Insights.

    // The Application Insights SDK adds a default logging filter that instructs ILogger to capture only Warning and more severe logs. Application Insights requires an explicit override. 
    // Log levels can also be configured using appsettings.json. For more information, see https://learn.microsoft.com/en-us/azure/azure-monitor/app/worker-service#ilogger-logs
    {
        _ = logging.Services.Configure<LoggerFilterOptions>(static options =>
        {
            LoggerFilterRule? defaultRule = options?.Rules?.FirstOrDefault(static rule => rule.ProviderName
                == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                _ = options?.Rules.Remove(defaultRule);
            }
        });
    })
    .Build();
host.Run();