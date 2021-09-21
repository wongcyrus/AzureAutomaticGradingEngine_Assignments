﻿using Microsoft.Azure.Management.ApplicationInsights.Management;
using Microsoft.Azure.Management.ApplicationInsights.Management.Models;
using NUnit.Framework;
using System.Linq;
using System.Net.Http;

namespace AzureProjectGrader
{
    class ApplicationInsightTest
    {
        private ApplicationInsightsManagementClient client;
        private ApplicationInsightsComponent applicationInsight;


        public ApplicationInsightTest()
        {
            Setup();
        }

        public ApplicationInsightsComponent GetApplicationInsights()
        {
            return client.Components.List().FirstOrDefault(c => c.Tags.ContainsKey("key") && c.Tags["key"] == "ApplicationInsights");
        }

        [SetUp]
        public void Setup()
        {
            var config = new Config();
            client = new ApplicationInsightsManagementClient(config.Credentials, new HttpClient(), true);
            client.SubscriptionId = config.SubscriptionId;
            applicationInsight = GetApplicationInsights();
        }

        [Test]
        public void Test01_AppServicePlanSettings()
        {
            Assert.AreEqual("southeastasia", applicationInsight.Location);
            Assert.AreEqual("other", applicationInsight.ApplicationType);
            Assert.AreEqual(30, applicationInsight.RetentionInDays);
        }
    }
}
