using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using NUnit.Framework;

namespace AzureProjectTestLib.Helper;

public class Config
{
    public Config()
    {
        var subscriptionId = TestContext.Parameters.Get("SubscriptionId", null);
        var trace = TestContext.Parameters.Get("trace", null);
        TestContext.Out.WriteLine(trace);

        if (!Guid.TryParse(subscriptionId, out _))
        {
            throw new InvalidOperationException("A valid SubscriptionId NUnit parameter is required.");
        }

        SubscriptionId = subscriptionId;
        TokenCredential = CreateTokenCredential();
        ArmClient = new ArmClient(TokenCredential, SubscriptionId);
    }

    private static TokenCredential CreateTokenCredential()
    {
        var managedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
    }

    public TokenCredential TokenCredential { get; }
    public ArmClient ArmClient { get; }
    public string SubscriptionId { get; }

    public SubscriptionResource GetSubscriptionResource()
    {
        return ArmClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(SubscriptionId));
    }

    public ResourceGroupResource GetResourceGroupResource(string resourceGroupName)
    {
        return ArmClient.GetResourceGroupResource(
            ResourceGroupResource.CreateResourceIdentifier(SubscriptionId, resourceGroupName));
    }
}