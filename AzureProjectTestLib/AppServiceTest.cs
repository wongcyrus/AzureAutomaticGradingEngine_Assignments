#pragma warning disable CS0618
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Storage;
using Azure.Storage.Queues;
using AzureProjectTestLib.Helper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace AzureProjectTestLib;

[GameClass(5)]
internal class AppServiceTest
{
    private HttpClient httpClient = null!;
    private AppServicePlanResource? appServicePlan;
    private WebSiteResource? functionApp;
    private StorageAccountResource? storageAccount;

    public AppServiceTest()
    {
        Setup();
    }

    [SetUp]
    public void Setup()
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(85)
        };

        var config = new Config();
        var resourceGroup = config.GetResourceGroupResource(Constants.ResourceGroupName);

        appServicePlan = resourceGroup
            .GetAppServicePlans()
            .GetAll()
            .FirstOrDefault(c =>
                c.Data.Tags.TryGetValue("key", out var tagValue) &&
                tagValue == "AppServicePlan");
        functionApp = resourceGroup
            .GetWebSites()
            .GetAll()
            .FirstOrDefault(c =>
                c.Data.Tags.TryGetValue("key", out var tagValue) &&
                tagValue == "FunctionApp");

        var storageAccountTest = new StorageAccountTest();
        storageAccount = storageAccountTest.GetLogicStorageAccount(storageAccountTest.GetStorageAccounts());
        storageAccountTest.TearDown();
    }

    [GameTask(
        "In resource group 'projProd', create a Windows App Service plan in the Azure Southeast Asia region with SKU tier 'Dynamic' and SKU name 'Y1', and tag it 'key'='AppServicePlan'. " +
        "Create a Function App resource in the same resource group and tag it 'key'='FunctionApp'.",
    10, 20, 1)]
    [Test]
    public void Test01_AppServicePlanWithTag()
    {
        Assert.IsNotNull(appServicePlan, "AppService Plans with tag {key:AppServicePlan}.");
    }

    [GameTask(1)]
    [Test]
    public void Test02_FunctionAppsWithTag()
    {
        Assert.IsNotNull(functionApp, "Function App Plans with tag {key:FunctionApp}.");
    }

    [GameTask(1)]
    [Test]
    public void Test03_AppServicePlanSettings()
    {
        Assert.AreEqual("southeastasia", appServicePlan!.Data.Location.ToString());
        Assert.AreEqual("Dynamic", appServicePlan.Data.Sku?.Tier);
        Assert.AreEqual("Y1", appServicePlan.Data.Sku?.Name);
        Assert.AreEqual("Windows", appServicePlan.Data.IsReserved == true ? "Linux" : "Windows");
    }
    #pragma warning restore CS0618

    [GameTask(
        "Configure the Function App in the Azure Southeast Asia region for Azure Functions v4 and Node.js 16 by setting FUNCTIONS_EXTENSION_VERSION='~4', FUNCTIONS_WORKER_RUNTIME='node', and WEBSITE_NODE_DEFAULT_VERSION='~16'. " +
        "Set WEBSITE_RUN_FROM_PACKAGE to a URL that starts with https://{logicStorageAccountName}.blob.core.windows.net/code/ and ends with app.zip. " +
        "Set StorageConnectionAppSetting and WEBSITE_CONTENTAZUREFILECONNECTIONSTRING to connection strings beginning with DefaultEndpointsProtocol=https;AccountName={logicStorageAccountName};AccountKey= for the Storage account tagged 'usage'='logic'.",
5, 20)]
    [Test]
    public void Test04_FunctionAppSettings()
    {
        Assert.AreEqual("southeastasia", functionApp!.Data.Location.ToString());
        var appSettings = GetAppSettings(functionApp);
        Assert.AreEqual("~4", appSettings["FUNCTIONS_EXTENSION_VERSION"]);
        Assert.AreEqual("node", appSettings["FUNCTIONS_WORKER_RUNTIME"]);
        Assert.AreEqual("~16", appSettings["WEBSITE_NODE_DEFAULT_VERSION"]);
        StringAssert.StartsWith($"https://{storageAccount!.Data.Name}.blob.core.windows.net/code/",
            appSettings["WEBSITE_RUN_FROM_PACKAGE"]);
        StringAssert.EndsWith("app.zip", appSettings["WEBSITE_RUN_FROM_PACKAGE"]);
        StringAssert.StartsWith($"DefaultEndpointsProtocol=https;AccountName={storageAccount.Data.Name};AccountKey=",
            appSettings["StorageConnectionAppSetting"]);
        StringAssert.StartsWith($"DefaultEndpointsProtocol=https;AccountName={storageAccount.Data.Name};AccountKey=",
            appSettings["WEBSITE_CONTENTAZUREFILECONNECTIONSTRING"]);
    }

    [GameTask(
        "Set the Function App setting APPINSIGHTS_INSTRUMENTATIONKEY to the instrumentation key of the Application Insights component tagged 'key'='ApplicationInsights'.",
5, 10)]
    [Test]
    public void Test05_FunctionAppSettingsInstrumentationKey()
    {
        var applicationInsightTest = new ApplicationInsightTest();
        var appSettings = GetAppSettings(functionApp!);
        Assert.AreEqual(applicationInsightTest.GetApplicationInsights()!.Data.InstrumentationKey,
            appSettings["APPINSIGHTS_INSTRUMENTATIONKEY"]);
    }

    [GameTask(
        "Configure a function in the Function App with this exact binding JSON: " +
 "{\"disabled\":false,\"bindings\":[{\"type\":\"httpTrigger\",\"name\":\"req\",\"direction\":\"in\",\"dataType\":\"string\",\"authLevel\":\"anonymous\",\"methods\":[\"get\"]},{\"type\":\"http\",\"direction\":\"out\",\"name\":\"res\"},{\"type\":\"queue\",\"name\":\"jobQueue\",\"queueName\":\"job\",\"direction\":\"out\",\"connection\":\"StorageConnectionAppSetting\"},{\"tableName\":\"message\",\"name\":\"messageTable\",\"type\":\"table\",\"direction\":\"out\",\"connection\":\"StorageConnectionAppSetting\"}]}",
5, 10)]
    [Test]
    public void Test04_AzureFunctionBinding()
    {
        var helloFunction = GetHelloFunction(functionApp!);
        const string functionJs = "{\"disabled\":false,\"bindings\":[{\"type\":\"httpTrigger\",\"name\":\"req\",\"direction\":\"in\",\"dataType\":\"string\",\"authLevel\":\"anonymous\",\"methods\":[\"get\"]},{\"type\":\"http\",\"direction\":\"out\",\"name\":\"res\"},{\"type\":\"queue\",\"name\":\"jobQueue\",\"queueName\":\"job\",\"direction\":\"out\",\"connection\":\"StorageConnectionAppSetting\"},{\"tableName\":\"message\",\"name\":\"messageTable\",\"type\":\"table\",\"direction\":\"out\",\"connection\":\"StorageConnectionAppSetting\"}]}";
        Assert.AreEqual(CanonicalizeJson(functionJs), CanonicalizeJson(helloFunction.Data.Config.ToString()));
    }

    [GameTask(
        "Update a Node.js Azure Function source code: when receiving a GET request ?user=tester&message=<value>, return 'Hello, tester and I received your message: <value>'",
10, 10)]
    [Test]
    public async Task Test05_AzureFunctionCallWithHttpResponse()
    {
        var helloFunction = GetHelloFunction(functionApp!);
        var message = DateTime.Now.ToString("yyyy’-‘MM’-‘dd’T’HH’:’mm’:’ss");
        var url = helloFunction.Data.InvokeUrlTemplate + "?user=tester&message=" + message;
        var helloResponse = await httpClient.GetStringAsync(url);
        var expected = $@"Hello, tester and I received your message: {message}";
        Assert.AreEqual(expected, helloResponse);
    }

    [GameTask(
    "Update a Node.js Azure Function source code: when receiving a GET request ?user=tester&message=<value>, then save PartitionKey 'tester' and RowKey '<value>' into Azure Table 'message'.",
10, 10)]
    [Test]
    public async Task Test06_AzureFunctionCallSaveDataToAzureTable()
    {
        var helloFunction = GetHelloFunction(functionApp!);
        var message = DateTime.Now.ToString("yyyy’-‘MM’-‘dd’T’HH’:’mm’:’ss");
        var url = helloFunction.Data.InvokeUrlTemplate + "?user=tester&message=" + message;
        await httpClient.GetStringAsync(url);

        var appSettings = await GetAppSettingsAsync(functionApp!);
        var connectionString = appSettings["StorageConnectionAppSetting"];

        var tableClient = new TableClient(connectionString, "message");
        var result = await tableClient.GetEntityIfExistsAsync<TableEntity>("tester", message);
        Assert.IsTrue(result.HasValue);
    }

    [GameTask(
        "Update the function so a GET request to ?user=tester&message=<value> adds a message to Azure Storage queue 'job' whose decoded text contains exactly \"message\": \"<value>\".",
10, 10)]
    [Test]
    public async Task Test07_AzureFunctionCallPutMessageToQueue()
    {
        var helloFunction = GetHelloFunction(functionApp!);

        var appSettings = await GetAppSettingsAsync(functionApp!);
        var connectionString = appSettings["StorageConnectionAppSetting"];

        var queueClient = new QueueClient(connectionString, "job");
        await queueClient.ClearMessagesAsync();

        var message = DateTime.Now.ToString("yyyy’-‘MM’-‘dd’T’HH’:’mm’:’ss");
        var url = helloFunction.Data.InvokeUrlTemplate + "?user=tester&message=" + message;
        await httpClient.GetStringAsync(url);

        var queueMessage = (await queueClient.ReceiveMessageAsync()).Value;

        Assert.IsNotNull(queueMessage);
        var resultAsString = DecodeQueueMessage(queueMessage!.MessageText);

        StringAssert.Contains($"\"message\": \"{message}\"", resultAsString);
    }

    private static SiteFunctionResource GetHelloFunction(WebSiteResource webSite)
    {
        return webSite.GetSiteFunctions().GetAll().First();
    }

    private static IReadOnlyDictionary<string, string> GetAppSettings(WebSiteResource webSite)
    {
        var appSettings = webSite.GetApplicationSettings().Value;
        return new Dictionary<string, string>(appSettings.Properties, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyDictionary<string, string>> GetAppSettingsAsync(WebSiteResource webSite)
    {
        var appSettings = (await webSite.GetApplicationSettingsAsync()).Value;
        return new Dictionary<string, string>(appSettings.Properties, StringComparer.OrdinalIgnoreCase);
    }

    private static string DecodeQueueMessage(string messageText)
    {
        if (messageText.Contains("\"message\":", StringComparison.Ordinal))
        {
            return messageText;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(messageText));
        }
        catch (FormatException)
        {
            return messageText;
        }
    }

    private static string CanonicalizeJson(string json)
    {
        return CanonicalizeJsonNode(JsonNode.Parse(json));
    }

    private static string CanonicalizeJsonNode(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonObject jsonObject => "{" + string.Join(",",
                jsonObject
                    .OrderBy(property => property.Key, StringComparer.Ordinal)
                    .Select(property =>
                        $"{JsonSerializer.Serialize(property.Key)}:{CanonicalizeJsonNode(property.Value)}")) + "}",
            JsonArray jsonArray => "[" + string.Join(",",
                jsonArray.Select(CanonicalizeJsonNode)) + "]",
            _ => node.ToJsonString()
        };
    }
}
