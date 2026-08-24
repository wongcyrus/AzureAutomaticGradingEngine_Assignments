using Azure.Core;
using Azure.Identity;
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

    public static async Task<bool> HasStudentSubscriptionAccessAsync(
        string subscriptionId,
        string studentEmail)
    {
        var armClient = new ArmClient(GetTokenCredential(), subscriptionId);
        var resourceGroupName =
            Environment.GetEnvironmentVariable("ASSIGNMENT_RESOURCE_GROUP") ?? "projProd";
        var resourceGroup = armClient.GetResourceGroupResource(
            ResourceGroupResource.CreateResourceIdentifier(
                subscriptionId,
                resourceGroupName));
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
