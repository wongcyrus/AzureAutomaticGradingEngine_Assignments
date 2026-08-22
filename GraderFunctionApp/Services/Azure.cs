using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Rest;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace GraderFunctionApp.Services;

internal static class Azure
{
    public static TokenCredential GetTokenCredential()
    {
        var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
    }

    public static async Task<IAzure> Get()
    {
        var defaultCredential = GetTokenCredential();
        var defaultToken = (await defaultCredential
            .GetTokenAsync(
                new TokenRequestContext(["https://management.azure.com/.default"]),
                CancellationToken.None)).Token;
        var defaultTokenCredentials = new TokenCredentials(defaultToken);
        var azureCredentials = new AzureCredentials(defaultTokenCredentials, defaultTokenCredentials, null,
            AzureEnvironment.AzureGlobalCloud);

        var azure = await Microsoft.Azure.Management.Fluent.Azure.Authenticate(azureCredentials)
            .WithDefaultSubscriptionAsync();
        return azure;
    }
    public static async Task<bool> HasStudentSubscriptionAccessAsync(
        string subscriptionId,
        string studentEmail)
    {
        var armClient = new ArmClient(GetTokenCredential(), subscriptionId);
        var resourceGroupName =
            Environment.GetEnvironmentVariable("ASSIGNMENT_RESOURCE_GROUP") ?? "projProd";
        var resourceGroup = armClient.GetResourceGroupResource(
            new ResourceIdentifier(
                $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}"));
        var response = await resourceGroup.GetAsync();
        var registeredEmail = response.Value.Data.Tags
            .FirstOrDefault(tag => string.Equals(
                tag.Key,
                "GradingStudentEmail",
                StringComparison.OrdinalIgnoreCase))
            .Value;

        return string.Equals(
            registeredEmail,
            studentEmail,
            StringComparison.OrdinalIgnoreCase);
    }
}