using Azure.Core;
using Azure.Identity;
using GraderFunctionApp.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Rest;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Resources;

namespace GraderFunctionApp.Services;

internal static class Azure
{
    public static async Task<IAzure> Get()
    {
        var defaultCredential = new DefaultAzureCredential();
        var defaultToken = (await defaultCredential
            .GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]))).Token;
        var defaultTokenCredentials = new TokenCredentials(defaultToken);
        var azureCredentials = new AzureCredentials(defaultTokenCredentials, defaultTokenCredentials, null,
            AzureEnvironment.AzureGlobalCloud);

        var azure = await Microsoft.Azure.Management.Fluent.Azure.Authenticate(azureCredentials)
            .WithDefaultSubscriptionAsync();
        return azure;
    }
    public static async Task<bool> IsValidSubscriptionReaderRole(Credential credential,
        string subscriptionId)
    {
        var armClient = new ArmClient(
            new ClientSecretCredential(
                credential.Tenant,
                credential.AppId,
                credential.Password));

        var scope = $"/subscriptions/{subscriptionId}";
        var roleAssignments = armClient.GetRoleAssignments(new ResourceIdentifier(scope));

        var readerRoleId = $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7";

        await foreach (var assignment in roleAssignments.GetAllAsync())
        {
              if (!string.IsNullOrEmpty(assignment.Data.RoleDefinitionId?.ToString()) &&
                assignment.Data.RoleDefinitionId.ToString().Equals(readerRoleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}