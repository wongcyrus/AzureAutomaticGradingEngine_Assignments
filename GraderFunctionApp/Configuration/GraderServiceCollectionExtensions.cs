using Azure.Data.Tables;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraderFunctionApp.Configuration;

public static class GraderServiceCollectionExtensions
{
    public static IServiceCollection AddGraderServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<string?>? getStorageConnectionString = null)
    {
        getStorageConnectionString ??= () =>
            Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        services.AddApplicationInsightsTelemetryWorkerService();
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionName));
        services.Configure<TestRunnerOptions>(
            configuration.GetSection(TestRunnerOptions.SectionName));

        services.AddSingleton<IStorageService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<StorageService>>();
            var connectionString = GetRequiredStorageConnectionString(
                getStorageConnectionString);
            return new StorageService(
                connectionString,
                logger,
                provider.GetRequiredService<IOptions<StorageOptions>>());
        });

        services.AddSingleton<IGameStateService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<GameStateService>>();
            var connectionString = GetRequiredStorageConnectionString(
                getStorageConnectionString);
            return new GameStateService(
                new TableServiceClient(connectionString),
                logger);
        });

        services.AddSingleton(_ => new TableServiceClient(
            GetRequiredStorageConnectionString(getStorageConnectionString)));
        services.AddSingleton<IGameTaskService, GameTaskService>();
        services.AddSingleton<IAIService>(provider => new AIService(
            provider.GetRequiredService<ILogger<AIService>>(),
            provider,
            provider.GetRequiredService<IStorageService>()));
        services.AddSingleton<IPreGeneratedMessageService>(provider =>
            new PreGeneratedMessageService(
                provider.GetRequiredService<ILogger<PreGeneratedMessageService>>(),
                provider.GetRequiredService<TableServiceClient>(),
                provider.GetRequiredService<IOptions<StorageOptions>>(),
                provider.GetRequiredService<IAIService>(),
                provider.GetRequiredService<IGameTaskService>()));
        services.AddSingleton<IUnifiedMessageService, UnifiedMessageService>();
        services.AddSingleton<IRequestAuthenticator, SignedRequestAuthenticator>();
        services.AddSingleton<ITestResultParser, TestResultParser>();
        services.AddSingleton<ITestRunner, TestRunner>();
        services.AddSingleton<GameTaskFunction>();

        return services;
    }

    private static string GetRequiredStorageConnectionString(
        Func<string?> getStorageConnectionString)
    {
        return getStorageConnectionString()
            ?? throw new InvalidOperationException(
                "AzureWebJobsStorage connection string not found");
    }
}
