#pragma warning disable CS0618
using Azure.Core;
using Azure.ResourceManager.ApplicationInsights;
using AzureProjectTestLib.Helper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AzureProjectTestLib;

[GameClass(4)]
[Parallelizable(ParallelScope.Children)]
internal class ApplicationInsightTest
{
    private ApplicationInsightsComponentResource? applicationInsight;

    public ApplicationInsightTest()
    {
        Setup();
    }

    public ApplicationInsightsComponentResource? GetApplicationInsights()
    {
        return applicationInsight;
    }

    [SetUp]
    public void Setup()
    {
        var config = new Config();
        var resourceGroup = config.GetResourceGroupResource(Constants.ResourceGroupName);
        applicationInsight = resourceGroup
            .GetApplicationInsightsComponents()
            .GetAll()
            .FirstOrDefault(c =>
                c.Data.Tags.TryGetValue("key", out var tagValue) &&
                tagValue == "ApplicationInsights");
    }

    [GameTask(
    "Create an Application Insights component in the Azure East Asia region with application type 'other', 30-day data retention, and tag 'key'='ApplicationInsights'.",
    3, 10, 1)]
    [Test]
    public void Test01_AppServicePlanWithTag()
    {
        Assert.IsNotNull(applicationInsight, "Application Insights with tag {key:ApplicationInsights}.");
    }

    [GameTask(1)]
    [Test]
    public void Test02_AppServicePlanSettings()
    {
        Assert.AreEqual(AzureLocation.EastAsia.ToString(), applicationInsight!.Data.Location.ToString());
        Assert.AreEqual("other", applicationInsight.Data.ApplicationType?.ToString().ToLowerInvariant());
        Assert.AreEqual(30, applicationInsight.Data.RetentionInDays);
    }
}
#pragma warning restore CS0618
