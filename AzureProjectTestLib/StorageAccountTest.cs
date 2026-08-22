#pragma warning disable CS0618
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using AzureProjectTestLib.Helper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AzureProjectTestLib;

[GameClass(2)]
internal class StorageAccountTest
{
    private static readonly HttpClient HttpClient = new();
    private StorageAccountResource? storageAccount;
    private StorageAccountResource? webStorageAccount;

    public StorageAccountTest()
    {
        Setup();
    }

    [SetUp]
    public void Setup()
    {
        var storageAccounts = GetStorageAccounts();
        storageAccount = GetLogicStorageAccount(storageAccounts);
        webStorageAccount = storageAccounts.FirstOrDefault(c =>
            c.Data.Tags.TryGetValue("usage", out var tagValue) &&
            tagValue == "StaticWeb");
    }

    public StorageAccountResource? GetLogicStorageAccount(IEnumerable<StorageAccountResource>? storageAccounts)
    {
        return storageAccounts?.FirstOrDefault(c =>
            c.Data.Tags.TryGetValue("usage", out var tagValue) &&
            tagValue == "logic");
    }

    public IReadOnlyList<StorageAccountResource> GetStorageAccounts()
    {
        var config = new Config();
        return config
            .GetResourceGroupResource(Constants.ResourceGroupName)
            .GetStorageAccounts()
            .GetAll()
            .ToList();
    }

    public TableResource? GetMessageTable()
    {
        if (storageAccount is null)
        {
            return null;
        }

        var tableResponse = storageAccount
            .GetTableService()
            .GetTables()
            .GetIfExists("message");

        return tableResponse.HasValue ? tableResponse.Value : null;
    }

    public StorageQueueResource? GetJobQueue()
    {
        if (storageAccount is null)
        {
            return null;
        }

        var queueResponse = storageAccount
            .GetQueueService()
            .GetStorageQueues()
            .GetIfExists("job");

        return queueResponse.HasValue ? queueResponse.Value : null;
    }

    [TearDown]
    public void TearDown()
    {
    }

    [GameTask(
        "Create a Storage account in resource group 'projProd' with tag 'usage'='logic'.",
        2, 10)]
    [Test]
    public void Test01_StorageAccountsWithTag()
    {
        Assert.IsNotNull(storageAccount, "StorageAccount Plans with tag {usage:logic}.");
    }

    [GameTask(
        "Create a Storage account in resource group 'projProd' with tag 'usage'='StaticWeb'.",
        2, 10)]
    [Test]
    public void Test02_StorageAccountsWithTag()
    {
        Assert.IsNotNull(webStorageAccount, "Static Web StorageAccount Plans with tag {usage:StaticWeb}.");
    }

    [GameTask(
        "Configure the Storage account tagged 'usage'='logic' in the Azure Southeast Asia region with Hot access tier, StorageV2 kind, Standard_LRS SKU, and public blob access enabled.",
        2, 20)]
    [Test]
    public void Test03_StorageAccountSettings()
    {
        Assert.IsNotNull(storageAccount, "StorageAccount Plans with tag {usage:logic}.");
        Assert.AreEqual("southeastasia", storageAccount?.Data.Location.ToString());
        Assert.AreEqual("Hot", storageAccount?.Data.AccessTier?.ToString());
        Assert.AreEqual("StorageV2", storageAccount?.Data.Kind.ToString());
        Assert.AreEqual("Standard_LRS", storageAccount?.Data.Sku.Name.ToString());
        Assert.IsTrue(storageAccount?.Data.AllowBlobPublicAccess ?? false);
    }

    [GameTask(
        "Configure the Storage account tagged 'usage'='StaticWeb' in the Azure East Asia region with Hot access tier, StorageV2 kind, Standard_LRS SKU, and public blob access enabled. " +
        "Enable static website hosting so its root returns exactly 'This is index page.' and a missing page returns HTTP 404 with the body exactly 'This is error page.'.", 2, 30)]
    [Test]
    public async Task Test04_WebStorageAccountSettings()
    {
        Assert.IsNotNull(webStorageAccount, "Static Web StorageAccount Plans with tag {usage:StaticWeb}.");
        Assert.AreEqual("eastasia", webStorageAccount!.Data.Location.ToString());
        Assert.AreEqual("Hot", webStorageAccount.Data.AccessTier?.ToString());
        Assert.AreEqual("StorageV2", webStorageAccount.Data.Kind.ToString());
        Assert.AreEqual("Standard_LRS", webStorageAccount.Data.Sku.Name.ToString());
        Assert.IsTrue(webStorageAccount.Data.AllowBlobPublicAccess ?? false);

        var webContainerResponse = webStorageAccount
            .GetBlobService()
            .GetBlobContainers()
            .GetIfExists("$web");
        Assert.IsTrue(webContainerResponse.HasValue);

        var webUrl = webStorageAccount.Data.PrimaryEndpoints?.WebUri?.ToString().TrimEnd('/');
        Assert.IsNotNull(webUrl);
        var resolvedWebUrl = webUrl!;

        var index = await HttpClient.GetStringAsync(resolvedWebUrl);
        Assert.AreEqual("This is index page.", index);

        AsyncTestDelegate requestMissingPage = async () =>
        {
            _ = await HttpClient.GetStringAsync(resolvedWebUrl + "/PageIsNotExist" + DateTime.Now.Ticks);
        };
        var ex = Assert.ThrowsAsync<HttpRequestException>(requestMissingPage);

        Assert.AreEqual("Response status code does not indicate success: 404 (The requested content does not exist.).",
            ex!.Message);

        var response = await HttpClient.GetAsync(resolvedWebUrl + "/PageIsNotExist" + DateTime.Now.Ticks);
        var error = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("This is error page.", error);
    }

    [GameTask("Create a Blob container named 'code' with anonymous Blob-level public access in the Storage account tagged 'usage'='logic'.", 2,
        10)]
    [Test]
    public void Test05_StorageAccountCodeContainer()
    {
        var codeContainerResponse = storageAccount!
            .GetBlobService()
            .GetBlobContainers()
            .GetIfExists("code");
        Assert.IsTrue(codeContainerResponse.HasValue);
        var codeContainer = codeContainerResponse.HasValue ? codeContainerResponse.Value : null;
        Assert.IsNotNull(codeContainer);
        Assert.AreEqual("Blob", codeContainer!.Data.PublicAccess?.ToString());
    }
    #pragma warning restore CS0618

    [GameTask("Create an Azure Table named 'message' in the Storage account tagged 'usage'='logic'.", 2,
        10)]
    [Test]
    public void Test06_StorageAccountMessageTable()
    {
        var messageTable = GetMessageTable();
        Assert.IsNotNull(messageTable);
    }

    [GameTask("Create an Azure Storage queue named 'job' in the Storage account tagged 'usage'='logic'.",
        2, 10)]
    [Test]
    public void Test07_StorageAccountJobQueue()
    {
        var jobQueue = GetJobQueue();
        Assert.IsNotNull(jobQueue);
    }
}
