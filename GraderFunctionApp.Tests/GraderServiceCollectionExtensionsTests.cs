using Azure.Data.Tables;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GraderFunctionApp.Tests;

public class GraderServiceCollectionExtensionsTests
{
    private const string ConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=" +
        "MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDA=;" +
        "EndpointSuffix=core.windows.net";

    [Test]
    public void AddGraderServices_RegistersApplicationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddGraderServices(
            new ConfigurationBuilder().Build(),
            () => ConnectionString);

        using var provider = services.BuildServiceProvider();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(services));
            Assert.That(provider.GetRequiredService<IStorageService>(), Is.TypeOf<StorageService>());
            Assert.That(provider.GetRequiredService<TableServiceClient>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<ITestResultParser>(), Is.TypeOf<TestResultParser>());
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IGameStateService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IGameTaskService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IAIService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IPreGeneratedMessageService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IUnifiedMessageService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IRequestAuthenticator)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(ITestRunner)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(GameTaskFunction)), Is.True);
        }
    }

    [Test]
    public void AddGraderServices_MissingStorageConfiguration_FailsExplicitly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraderServices(
            new ConfigurationBuilder().Build(),
            () => null);
        using var provider = services.BuildServiceProvider();

        Action action = () => provider.GetRequiredService<IStorageService>();
        var exception = Assert.Throws<InvalidOperationException>(action);

        Assert.That(exception?.Message, Does.Contain("AzureWebJobsStorage"));
    }
}
