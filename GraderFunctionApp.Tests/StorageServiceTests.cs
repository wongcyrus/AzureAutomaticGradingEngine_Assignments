using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Models;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class StorageServiceTests
{
    private TableServiceClient tableServiceClient = null!;
    private BlobServiceClient blobServiceClient = null!;
    private TableClient tableClient = null!;
    private TableClient gameStateTableClient = null!;
    private StorageService service = null!;

    [SetUp]
    public void SetUp()
    {
        tableServiceClient = Substitute.For<TableServiceClient>();
        blobServiceClient = Substitute.For<BlobServiceClient>();
        tableClient = Substitute.For<TableClient>();
        gameStateTableClient = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("GameStates")
            .Returns(gameStateTableClient);
        var missingResetMarker = AzureTestResponses.Missing<GameResetMarker>();
        gameStateTableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Arg.Any<string>(),
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(missingResetMarker);
        service = new StorageService(
            blobServiceClient,
            tableServiceClient,
            NullLogger<StorageService>.Instance,
            Options.Create(new StorageOptions()));
    }

    [Test]
    public async Task GetPassedTasksAsync_ReturnsNamesAndMarks()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.QueryAsync<PassTestEntity>(
                Arg.Any<System.Linq.Expressions.Expression<Func<PassTestEntity, bool>>>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(
                new PassTestEntity { PartitionKey = "student", RowKey = "a", TestName = "A", Mark = 10 },
                new PassTestEntity { PartitionKey = "student", RowKey = "b", TestName = "B", Mark = 20 }));

        var result = await service.GetPassedTasksAsync("student");

        Assert.That(result, Is.EqualTo(new List<(string, int)> { ("A", 10), ("B", 20) }));
    }

    [Test]
    public async Task SaveTestResultXmlAsync_UploadsXmlAndReturnsSanitizedBlobName()
    {
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        blobServiceClient.GetBlobContainerClient("test-results").Returns(container);
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);

        var result = await service.SaveTestResultXmlAsync("student@example.com", "<test-run />");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("student@example.com_"));
            Assert.That(result, Does.EndWith(".xml"));
        }
        await container.Received(1).CreateIfNotExistsAsync(
            PublicAccessType.None, null, null, Arg.Any<CancellationToken>());
        await blob.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Is<BlobUploadOptions>(options => options.HttpHeaders.ContentType == "text/xml"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveTestResultXmlAsync_EmptyValues_UsesSafeDefaults()
    {
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        blobServiceClient.GetBlobContainerClient("test-results").Returns(container);
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);

        var result = await service.SaveTestResultXmlAsync("", "");

        Assert.That(result, Does.StartWith("noemail_"));
        await blob.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCompletedTaskNamesAsync_ReturnsUniqueNonEmptyTasks()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.QueryAsync<PassTestEntity>(
                Arg.Any<System.Linq.Expressions.Expression<Func<PassTestEntity, bool>>>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(
                new PassTestEntity { PartitionKey = "student", RowKey = "a", TaskName = "Task A" },
                new PassTestEntity { PartitionKey = "student", RowKey = "b", TaskName = "Task A" },
                new PassTestEntity { PartitionKey = "student", RowKey = "c", TaskName = "" }));

        var result = await service.GetCompletedTaskNamesAsync("student");

        Assert.That(result, Is.EqualTo(new[] { "Task A" }));
    }

    [Test]
    public async Task SavePassTestRecordAsync_StoresOnlyNonNegativeResults()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.GetEntityAsync<PassTestEntity>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns<Task<Response<PassTestEntity>>>(
                _ => throw new RequestFailedException(404, "Not found"));

        await service.SavePassTestRecordAsync(
            "student@example.com",
            "Task A",
            new Dictionary<string, int> { ["Namespace.Pass"] = 10, ["Namespace.Ignore"] = -1 },
            "Stella");

        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<ITableEntity>(entity =>
                entity.GetType() == typeof(PassTestEntity) &&
                ((PassTestEntity)entity).TestName == "Namespace.Pass" &&
                ((PassTestEntity)entity).Mark == 10 &&
                ((PassTestEntity)entity).AssignedByNPC == "Stella"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void SavePassTestRecordAsync_DuringReset_DoesNotRecreateProgress()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.GetEntityAsync<PassTestEntity>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns<Task<Response<PassTestEntity>>>(
                _ => throw new RequestFailedException(404, "Not found"));
        gameStateTableClient.GetEntityIfExistsAsync<GameResetMarker>(
                "student@example.com",
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                new GameResetMarker { PartitionKey = "student@example.com" },
                Substitute.For<Response>()));

        Func<Task> action = async () => await service.SavePassTestRecordAsync(
            "student@example.com",
            "Task A",
            new Dictionary<string, int> { ["Namespace.Pass"] = 10 },
            "Stella");

        Assert.ThrowsAsync<InvalidOperationException>(action);
        tableClient.DidNotReceiveWithAnyArgs().UpsertEntityAsync(
            default(ITableEntity)!,
            default,
            default);
    }

    [Test]
    public async Task SaveFailTestRecordAsync_StoresOnlyFailedResults()
    {
        tableServiceClient.GetTableClient("FailTests").Returns(tableClient);

        await service.SaveFailTestRecordAsync(
            "student@example.com",
            "Task A",
            new Dictionary<string, int> { ["Namespace.Pass"] = 1, ["Namespace.Fail"] = 0 },
            "Stella");

        await tableClient.Received(1).AddEntityAsync(
            Arg.Is<ITableEntity>(entity =>
                entity.GetType() == typeof(FailTestEntity) &&
                ((FailTestEntity)entity).TestName == "Namespace.Fail" &&
                ((FailTestEntity)entity).AssignedByNPC == "Stella" &&
                ((FailTestEntity)entity).RowKey.StartsWith("Fail_")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetFailedTestsAsync_ReturnsFailureHistory()
    {
        tableServiceClient.GetTableClient("FailTests").Returns(tableClient);
        var failure = new FailTestEntity
        {
            PartitionKey = "student@example.com",
            RowKey = "failure",
            TestName = "Test01"
        };
        tableClient.QueryAsync<FailTestEntity>(
                Arg.Any<System.Linq.Expressions.Expression<Func<FailTestEntity, bool>>>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(failure));

        var result = await service.GetFailedTestsAsync("student@example.com");

        Assert.That(result, Is.EqualTo(new[] { failure }));
    }

    [Test]
    public async Task DeletePassedTasksAsync_DeletesPartitionAndVerifiesItIsEmpty()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        var entity = new TableEntity("student@example.com", "test");
        tableClient.QueryAsync<TableEntity>(
                Arg.Any<string>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(
                AzureTestResponses.AsyncPageable(entity),
                AzureTestResponses.AsyncPageable<TableEntity>());

        var result = await service.DeletePassedTasksAsync("student@example.com");

        Assert.That(result, Is.EqualTo(1));
        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                actions.Count() == 1 &&
                actions.Single().ActionType == TableTransactionActionType.Delete),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetSubscriptionIdAsync_FixedRegistration_ReturnsSubscription()
    {
        tableServiceClient.GetTableClient("Subscription").Returns(tableClient);
        var registration = new Subscription
        {
            PartitionKey = "student@example.com",
            RowKey = Subscription.RegistrationRowKey,
            SubscriptionId = Guid.NewGuid().ToString()
        };
        tableClient.GetEntityIfExistsAsync<Subscription>(
                "student@example.com",
                Subscription.RegistrationRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(registration, Substitute.For<Response>()));

        var result = await service.GetSubscriptionIdAsync("student@example.com");

        Assert.That(result, Is.EqualTo(registration.SubscriptionId));
    }

    [Test]
    public async Task GetSubscriptionIdAsync_NoRegistration_ReturnsNull()
    {
        tableServiceClient.GetTableClient("Subscription").Returns(tableClient);
        var missing = AzureTestResponses.Missing<Subscription>();
        tableClient.GetEntityIfExistsAsync<Subscription>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        tableClient.QueryAsync<Subscription>(
                Arg.Any<System.Linq.Expressions.Expression<Func<Subscription, bool>>>(),
                1,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable<Subscription>());

        var result = await service.GetSubscriptionIdAsync("student@example.com");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSubscriptionIdAsync_LegacyRegistration_ReturnsSubscription()
    {
        tableServiceClient.GetTableClient("Subscription").Returns(tableClient);
        var missing = AzureTestResponses.Missing<Subscription>();
        tableClient.GetEntityIfExistsAsync<Subscription>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        var legacy = new Subscription
        {
            PartitionKey = "student@example.com",
            RowKey = Guid.NewGuid().ToString(),
            SubscriptionId = Guid.NewGuid().ToString()
        };
        tableClient.QueryAsync<Subscription>(
                Arg.Any<System.Linq.Expressions.Expression<Func<Subscription, bool>>>(),
                1,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(legacy));

        var result = await service.GetSubscriptionIdAsync("student@example.com");

        Assert.That(result, Is.EqualTo(legacy.SubscriptionId));
    }

    [Test]
    public async Task GetLastTaskNPCAsync_ReturnsMostRecentNpc()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.QueryAsync<PassTestEntity>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(
                new PassTestEntity { PassedAt = DateTimeOffset.UtcNow.AddHours(-2), AssignedByNPC = "Stella" },
                new PassTestEntity { PassedAt = DateTimeOffset.UtcNow, AssignedByNPC = "Nova" }));

        var result = await service.GetLastTaskNPCAsync("student@example.com");

        Assert.That(result, Is.EqualTo("Nova"));
    }

    [Test]
    public async Task GetRandomEasterEggAsync_NoMatches_ReturnsNull()
    {
        tableServiceClient.GetTableClient("EasterEgg").Returns(tableClient);
        tableClient.QueryAsync<EasterEgg>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable<EasterEgg>());

        Assert.That(await service.GetRandomEasterEggAsync("Pass"), Is.Null);
    }

    [Test]
    public async Task GetRandomEasterEggAsync_Match_ReturnsLink()
    {
        tableServiceClient.GetTableClient("EasterEgg").Returns(tableClient);
        tableClient.QueryAsync<EasterEgg>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(new EasterEgg
            {
                PartitionKey = "Pass",
                RowKey = "one",
                Type = "Pass",
                Link = "https://example.com/pass"
            }));

        Assert.That(
            await service.GetRandomEasterEggAsync("Pass"),
            Is.EqualTo("https://example.com/pass"));
    }

    [Test]
    public async Task GetNPCCharacterAsync_ExistingNpc_ReturnsCharacter()
    {
        tableServiceClient.GetTableClient("NPCCharacter").Returns(tableClient);
        var npc = new NPCCharacter { RowKey = "Stella", Name = "Stella" };
        tableClient.GetEntityIfExistsAsync<NPCCharacter>(
                "NPC", "Stella", null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(npc, Substitute.For<Response>()));

        Assert.That(await service.GetNPCCharacterAsync("Stella"), Is.SameAs(npc));
    }

    [Test]
    public async Task GetNPCCharacterAsync_MissingNpc_ReturnsNull()
    {
        tableServiceClient.GetTableClient("NPCCharacter").Returns(tableClient);
        var missing = AzureTestResponses.Missing<NPCCharacter>();
        tableClient.GetEntityIfExistsAsync<NPCCharacter>(
                "NPC", "Unknown", null, Arg.Any<CancellationToken>())
            .Returns(missing);

        Assert.That(await service.GetNPCCharacterAsync("Unknown"), Is.Null);
    }

    [Test]
    public async Task GenerateTestResultSasUrlAsync_EmptyName_ReturnsNull()
    {
        Assert.That(await service.GenerateTestResultSasUrlAsync(""), Is.Null);
    }

    [Test]
    public async Task GenerateTestResultSasUrlAsync_MissingBlob_ReturnsNull()
    {
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        blobServiceClient.GetBlobContainerClient("test-results").Returns(container);
        container.GetBlobClient("result.xml").Returns(blob);
        blob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(false, Substitute.For<Response>()));

        Assert.That(await service.GenerateTestResultSasUrlAsync("result.xml"), Is.Null);
    }

    [Test]
    public async Task GenerateTestResultSasUrlAsync_ExistingBlob_ReturnsReadSas()
    {
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        var expected = new Uri("https://example.com/test-results/result.xml?sig=test");
        blobServiceClient.GetBlobContainerClient("test-results").Returns(container);
        container.GetBlobClient("result.xml").Returns(blob);
        blob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        blob.GenerateSasUri(Arg.Any<Azure.Storage.Sas.BlobSasBuilder>()).Returns(expected);

        var result = await service.GenerateTestResultSasUrlAsync("result.xml");

        Assert.That(result, Is.EqualTo(expected.ToString()));
        blob.Received(1).GenerateSasUri(
            Arg.Is<Azure.Storage.Sas.BlobSasBuilder>(builder =>
                builder.BlobContainerName == "test-results" &&
                builder.BlobName == "result.xml"));
    }

    [Test]
    public async Task GenerateTestResultSasUrlAsync_ClientFailure_ReturnsNull()
    {
        blobServiceClient.GetBlobContainerClient("test-results")
            .Returns<BlobContainerClient>(_ => throw new InvalidOperationException("storage unavailable"));

        Assert.That(await service.GenerateTestResultSasUrlAsync("result.xml"), Is.Null);
    }

    [Test]
    public void SaveTestResultXmlAsync_UploadFailure_Rethrows()
    {
        blobServiceClient.GetBlobContainerClient("test-results")
            .Returns<BlobContainerClient>(_ => throw new InvalidOperationException("upload unavailable"));

        Func<Task> action = async () =>
            await service.SaveTestResultXmlAsync("student@example.com", "<test-run />");

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task SavePassTestRecordAsync_ExistingRecord_SkipsWrite()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.GetEntityAsync<PassTestEntity>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(new PassTestEntity(), Substitute.For<Response>()));

        await service.SavePassTestRecordAsync(
            "student@example.com",
            "Task A",
            new Dictionary<string, int> { ["Namespace.Pass"] = 10 },
            "Stella");

        await tableClient.DidNotReceive().UpsertEntityAsync(
            Arg.Any<ITableEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SavePassTestRecordAsync_EmptyIdentityAndTest_UsesSafeKeys()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.GetEntityAsync<PassTestEntity>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns<Task<Response<PassTestEntity>>>(
                _ => throw new RequestFailedException(404, "Not found"));

        await service.SavePassTestRecordAsync(
            "",
            "Task A",
            new Dictionary<string, int> { [""] = 1 },
            "Stella");

        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<ITableEntity>(entity =>
                entity.PartitionKey == "noemail" && entity.RowKey == "invalidtest"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void SavePassTestRecordAsync_TableFailure_Rethrows()
    {
        tableServiceClient.GetTableClient("PassTests")
            .Returns<TableClient>(_ => throw new InvalidOperationException("table unavailable"));

        Func<Task> action = async () => await service.SavePassTestRecordAsync(
            "student@example.com", "Task", new Dictionary<string, int>(), "Stella");

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public void SaveFailTestRecordAsync_TableFailure_Rethrows()
    {
        tableServiceClient.GetTableClient("FailTests")
            .Returns<TableClient>(_ => throw new InvalidOperationException("table unavailable"));

        Func<Task> action = async () => await service.SaveFailTestRecordAsync(
            "student@example.com", "Task", new Dictionary<string, int>(), "Stella");

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public void GetPassedTasksAsync_QueryFailure_Rethrows()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.QueryAsync<PassTestEntity>(
                Arg.Any<System.Linq.Expressions.Expression<Func<PassTestEntity, bool>>>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns<AsyncPageable<PassTestEntity>>(_ => throw new InvalidOperationException("query failed"));

        Func<Task> action = async () =>
            await service.GetPassedTasksAsync("student@example.com");

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public void GetCompletedTaskNamesAsync_QueryFailure_Rethrows()
    {
        tableServiceClient.GetTableClient("PassTests").Returns(tableClient);
        tableClient.QueryAsync<PassTestEntity>(
                Arg.Any<System.Linq.Expressions.Expression<Func<PassTestEntity, bool>>>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns<AsyncPageable<PassTestEntity>>(_ => throw new InvalidOperationException("query failed"));

        Func<Task> action = async () =>
            await service.GetCompletedTaskNamesAsync("student@example.com");

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task GetLastTaskNPCAsync_QueryFailure_ReturnsNull()
    {
        tableServiceClient.GetTableClient("PassTests")
            .Returns<TableClient>(_ => throw new InvalidOperationException("query failed"));

        Assert.That(await service.GetLastTaskNPCAsync("student@example.com"), Is.Null);
    }

    [Test]
    public async Task GetRandomEasterEggAsync_QueryFailure_ReturnsNull()
    {
        tableServiceClient.GetTableClient("EasterEgg")
            .Returns<TableClient>(_ => throw new InvalidOperationException("query failed"));

        Assert.That(await service.GetRandomEasterEggAsync("Pass"), Is.Null);
    }

    [Test]
    public async Task GetNPCCharacterAsync_QueryFailure_ReturnsNull()
    {
        tableServiceClient.GetTableClient("NPCCharacter")
            .Returns<TableClient>(_ => throw new InvalidOperationException("query failed"));

        Assert.That(await service.GetNPCCharacterAsync("Stella"), Is.Null);
    }
}
