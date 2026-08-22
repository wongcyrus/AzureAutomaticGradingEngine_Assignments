using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Rest;
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

        var token = TokenCredential.GetToken(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            CancellationToken.None);
        var tokenCredentials = new TokenCredentials(token.Token);
        Credentials = new AzureCredentials(
            tokenCredentials,
            tokenCredentials,
            tenantId: null,
            AzureEnvironment.AzureGlobalCloud);
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
    public AzureCredentials Credentials { get; }
    public string SubscriptionId { get; }
}