using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class PreGeneratedMessageServiceTests
{
    private TableClient tableClient = null!;
    private TableServiceClient tableServiceClient = null!;
    private IAIService aiService = null!;
    private IGameTaskService gameTaskService = null!;
    private PreGeneratedMessageService service = null!;

    [SetUp]
    public void SetUp()
    {
        tableClient = Substitute.For<TableClient>();
        tableServiceClient = Substitute.For<TableServiceClient>();
        aiService = Substitute.For<IAIService>();
        gameTaskService = Substitute.For<IGameTaskService>();
        service = new PreGeneratedMessageService(
            NullLogger<PreGeneratedMessageService>.Instance,
            tableServiceClient,
            tableClient,
            Options.Create(new StorageOptions()),
            aiService,
            gameTaskService);
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_EmptyInput_ReturnsNull()
    {
        Assert.That(await service.GetPreGeneratedInstructionAsync(""), Is.Null);
    }

    [Test]
    public async Task Constructor_UsesConfiguredPreGeneratedMessageTable()
    {
        var configuredTableClient = Substitute.For<TableClient>();
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableServiceClient.GetTableClient("CustomMessages").Returns(configuredTableClient);
        configuredTableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);

        var serviceWithConfiguredTable = new PreGeneratedMessageService(
            NullLogger<PreGeneratedMessageService>.Instance,
            tableServiceClient,
            Options.Create(new StorageOptions
            {
                PreGeneratedMessageTableName = "CustomMessages"
            }),
            aiService,
            gameTaskService);

        Assert.That(await serviceWithConfiguredTable.GetPreGeneratedInstructionAsync("original"), Is.Null);
        tableServiceClient.Received(1).GetTableClient("CustomMessages");
        await configuredTableClient.Received(1).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_ExistingMessage_IncrementsHitCount()
    {
        var message = CreateMessage("instruction", "original", "generated", 2);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(message, Substitute.For<Response>()));

        var result = await service.GetPreGeneratedInstructionAsync("original");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("generated"));
            Assert.That(message.HitCount, Is.EqualTo(3));
            Assert.That(message.LastUsedAt, Is.Not.Null);
        }
        await tableClient.Received(1).UpdateEntityAsync(
            message, message.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_LookupThrows_ReturnsNull()
    {
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NullableResponse<PreGeneratedMessage>>(
                new InvalidOperationException("lookup failed")));

        Assert.That(await service.GetPreGeneratedInstructionAsync("original"), Is.Null);
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_ConcurrencyConflictDuringHitCountUpdate_ReturnsGeneratedMessage()
    {
        var message = CreateMessage("instruction", "original", "generated", 2);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(message, Substitute.For<Response>()));
        tableClient.UpdateEntityAsync(
                message, message.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(412, "Changed"));

        var result = await service.GetPreGeneratedInstructionAsync("original");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("generated"));
            Assert.That(message.HitCount, Is.EqualTo(3));
            Assert.That(message.LastUsedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_UnexpectedHitCountUpdateFailure_ReturnsGeneratedMessage()
    {
        var message = CreateMessage("instruction", "original", "generated", 2);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(message, Substitute.For<Response>()));
        tableClient.UpdateEntityAsync(
                message, message.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new InvalidOperationException("update failed"));

        Assert.That(await service.GetPreGeneratedInstructionAsync("original"), Is.EqualTo("generated"));
    }

    [Test]
    public async Task GetPreGeneratedInstructionAsync_MissingMessage_ReturnsNull()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);

        Assert.That(await service.GetPreGeneratedInstructionAsync("original"), Is.Null);
    }

    [Test]
    public async Task GetPreGeneratedNPCMessageAsync_EmptyInput_ReturnsNull()
    {
        Assert.That(await service.GetPreGeneratedNPCMessageAsync("", 20, "female", "engineer"), Is.Null);
    }

    [Test]
    public async Task GetPreGeneratedNPCMessageAsync_ExistingMessage_ReturnsGeneratedText()
    {
        var message = CreateMessage("npc", "original", "generated", 0);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(message, Substitute.For<Response>()));

        var result = await service.GetPreGeneratedNPCMessageAsync(
            "original", 20, "female", "engineer");

        Assert.That(result, Is.EqualTo("generated"));
    }

    [Test]
    public async Task GetPreGeneratedNPCMessageAsync_MissingMessage_ReturnsNull()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);

        Assert.That(await service.GetPreGeneratedNPCMessageAsync("original", 20, "female", "engineer"), Is.Null);
    }

    [Test]
    public async Task GetPreGeneratedNPCMessageAsync_LookupThrows_ReturnsNull()
    {
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NullableResponse<PreGeneratedMessage>>(
                new InvalidOperationException("lookup failed")));

        Assert.That(await service.GetPreGeneratedNPCMessageAsync("original", 20, "female", "engineer"), Is.Null);
    }

    [Test]
    public async Task GetPreGeneratedNPCMessageAsync_UsesExpectedCacheKey()
    {
        const string originalMessage = "Hello";
        const int age = 20;
        const string gender = "female";
        const string background = "engineer";
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();

        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);

        await service.GetPreGeneratedNPCMessageAsync(originalMessage, age, gender, background);

        await tableClient.Received(1).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "npc",
            BuildNpcCacheKey(originalMessage, age, gender, background),
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetHitCountStatsAsync_ComputesStatistics()
    {
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(
                CreateMessage("instruction", "a", "A", 3),
                CreateMessage("npc", "b", "B", 1),
                CreateMessage("npc", "c", "C", 0)));

        var stats = await service.GetHitCountStatsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.TotalMessages, Is.EqualTo(3));
            Assert.That(stats.TotalHits, Is.EqualTo(4));
            Assert.That(stats.UnusedMessages, Is.EqualTo(1));
            Assert.That(stats.InstructionMessages, Is.EqualTo(1));
            Assert.That(stats.NPCMessages, Is.EqualTo(2));
            Assert.That(stats.MostUsedMessage?.OriginalMessage, Is.EqualTo("a"));
            Assert.That(stats.LeastUsedMessage?.OriginalMessage, Is.EqualTo("c"));
        }
    }

    [Test]
    public async Task GetHitCountStatsAsync_QueryFails_ReturnsEmptyStats()
    {
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("query failed"));

        var stats = await service.GetHitCountStatsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.TotalMessages, Is.Zero);
            Assert.That(stats.TotalHits, Is.Zero);
            Assert.That(stats.MostUsedMessage, Is.Null);
            Assert.That(stats.LeastUsedMessage, Is.Null);
        }
    }

    [Test]
    public async Task ResetHitCountsAsync_ResetsMatchingMessages()
    {
        var message = CreateMessage("npc", "a", "A", 3);
        message.LastUsedAt = DateTime.UtcNow;
        tableClient.QueryAsync<PreGeneratedMessage>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(message));

        await service.ResetHitCountsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message.HitCount, Is.Zero);
            Assert.That(message.LastUsedAt, Is.Null);
        }
        await tableClient.Received(1).UpdateEntityAsync(
            message, message.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResetHitCountsAsync_ContinuesAfterConflictsAndUnexpectedFailures()
    {
        var conflict = CreateMessage("npc", "a", "A", 3);
        conflict.LastUsedAt = DateTime.UtcNow;
        var failure = CreateMessage("instruction", "b", "B", 2);
        failure.LastUsedAt = DateTime.UtcNow;
        var success = CreateMessage("npc", "c", "C", 1);
        success.LastUsedAt = DateTime.UtcNow;

        tableClient.QueryAsync<PreGeneratedMessage>(
                "HitCount gt 0", null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(conflict, failure, success));
        tableClient.UpdateEntityAsync(
                conflict, conflict.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(412, "Changed"));
        tableClient.UpdateEntityAsync(
                failure, failure.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new InvalidOperationException("update failed"));

        await service.ResetHitCountsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(conflict.HitCount, Is.Zero);
            Assert.That(conflict.LastUsedAt, Is.Null);
            Assert.That(failure.HitCount, Is.Zero);
            Assert.That(failure.LastUsedAt, Is.Null);
            Assert.That(success.HitCount, Is.Zero);
            Assert.That(success.LastUsedAt, Is.Null);
        }
        await tableClient.Received(1).UpdateEntityAsync(
            success, success.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Test]
    public void ResetHitCountsAsync_QueryFails_Rethrows()
    {
        tableClient.QueryAsync<PreGeneratedMessage>(
                "HitCount gt 0", null, null, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("query failed"));

        Func<Task> act = () => service.ResetHitCountsAsync();
        Assert.That(act, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ClearAllPreGeneratedMessagesAsync_DeletesMessages()
    {
        var first = CreateMessage("npc", "a", "A", 0);
        var second = CreateMessage("instruction", "b", "B", 0);
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(first, second));

        await service.ClearAllPreGeneratedMessagesAsync();

        await tableClient.Received(1).DeleteEntityAsync(
            first.PartitionKey, first.RowKey, first.ETag, Arg.Any<CancellationToken>());
        await tableClient.Received(1).DeleteEntityAsync(
            second.PartitionKey, second.RowKey, second.ETag, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ClearAllPreGeneratedMessagesAsync_NoMessages_DoesNotDelete()
    {
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable<PreGeneratedMessage>());

        await service.ClearAllPreGeneratedMessagesAsync();

        await tableClient.DidNotReceive().DeleteEntityAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ClearAllPreGeneratedMessagesAsync_ContinuesAfterDeleteFailures()
    {
        var missing = CreateMessage("npc", "a", "A", 0);
        var failure = CreateMessage("instruction", "b", "B", 0);
        var success = CreateMessage("npc", "c", "C", 0);
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(missing, failure, success));
        tableClient.DeleteEntityAsync(
                missing.PartitionKey, missing.RowKey, missing.ETag, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(404, "Not found"));
        tableClient.DeleteEntityAsync(
                failure.PartitionKey, failure.RowKey, failure.ETag, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new InvalidOperationException("delete failed"));

        await service.ClearAllPreGeneratedMessagesAsync();

        await tableClient.Received(1).DeleteEntityAsync(
            success.PartitionKey, success.RowKey, success.ETag, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ClearAllPreGeneratedMessagesAsync_MultipleBatches_DeletesEveryMessage()
    {
        var messages = Enumerable.Range(0, 101)
            .Select(index => CreateMessage("npc", $"original-{index}", $"generated-{index}", 0))
            .ToArray();
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(messages));

        await service.ClearAllPreGeneratedMessagesAsync();

        await tableClient.Received(101).DeleteEntityAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void ClearAllPreGeneratedMessagesAsync_QueryFails_Rethrows()
    {
        tableClient.QueryAsync<PreGeneratedMessage>(
                (string?)null, null, null, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("query failed"));

        Func<Task> act = () => service.ClearAllPreGeneratedMessagesAsync();
        Assert.That(act, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task RefreshAllPreGeneratedMessagesAsync_NoTasksOrNpcs_Completes()
    {
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>());
        var npcTable = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("NPCCharacter").Returns(npcTable);
        npcTable.QueryAsync<NPCCharacter>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable<NPCCharacter>());

        await service.RefreshAllPreGeneratedMessagesAsync();

        await tableClient.Received(1).CreateIfNotExistsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshAllPreGeneratedMessagesAsync_WithTasks_GeneratesOnlyNonEmptyInstructions()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>
        {
            CreateTask("Task 1", "Instruction 1"),
            CreateTask("Task 2", "Instruction 2"),
            CreateTask("Task 3", "Instruction 3"),
            CreateTask("Task 4", "Instruction 4"),
            CreateTask("Task 5", "Instruction 5"),
            CreateTask("Task 6", "Instruction 6"),
            CreateTask("Ignored", "")
        });
        var npcTable = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("NPCCharacter").Returns(npcTable);
        npcTable.QueryAsync<NPCCharacter>(
                "PartitionKey eq 'NPC'", null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable<NPCCharacter>());
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.RephraseInstructionAsync(Arg.Any<string>())
            .Returns(callInfo => $"{callInfo.ArgAt<string>(0)} rewritten");

        await service.RefreshAllPreGeneratedMessagesAsync();

        await aiService.Received(6).RephraseInstructionAsync(Arg.Any<string>());
        await aiService.DidNotReceive().RephraseInstructionAsync("");
        await tableClient.Received(6).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message => message.PartitionKey == "instruction"),
            Arg.Any<TableUpdateMode>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void RefreshAllPreGeneratedMessagesAsync_TaskLoadingFails_Rethrows()
    {
        gameTaskService.GetTasks(false).Returns(_ => throw new InvalidOperationException("task load failed"));

        Func<Task> act = () => service.RefreshAllPreGeneratedMessagesAsync();
        Assert.That(act, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RefreshAllPreGeneratedMessagesAsync_NpcQueryFails_Rethrows()
    {
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>());
        var npcTable = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("NPCCharacter").Returns(npcTable);
        npcTable.QueryAsync<NPCCharacter>(
                "PartitionKey eq 'NPC'", null, null, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("npc query failed"));

        Func<Task> act = () => service.RefreshAllPreGeneratedMessagesAsync();
        Assert.That(act, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task GenerateAndStoreInstructionAsync_NewGeneratedText_StoresMessage()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.RephraseInstructionAsync("Create a resource").Returns("Rephrased instruction");

        var generated = await service.GenerateAndStoreInstructionAsync("Create a resource");

        Assert.That(generated, Is.True);
        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message =>
                message.PartitionKey == "instruction" &&
                message.OriginalMessage == "Create a resource" &&
                message.GeneratedMessage == "Rephrased instruction"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateAndStoreInstructionAsync_TransientLookupFailure_RetriesAndStores()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<NullableResponse<PreGeneratedMessage>>(new InvalidOperationException("transient")),
                Task.FromResult(missing));
        aiService.RephraseInstructionAsync("Retry me").Returns("Retried instruction");

        await service.GenerateAndStoreInstructionAsync("Retry me");

        await tableClient.Received(2).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message =>
                message.OriginalMessage == "Retry me" &&
                message.GeneratedMessage == "Retried instruction"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateAndStoreInstructionAsync_PersistentLookupFailure_GivesUpWithoutCallingAI()
    {
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NullableResponse<PreGeneratedMessage>>(
                new InvalidOperationException("persistent failure")));

        var generated = await service.GenerateAndStoreInstructionAsync("Retry me");

        Assert.That(generated, Is.False);
        await tableClient.Received(3).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await aiService.DidNotReceive().RephraseInstructionAsync(Arg.Any<string>());
    }

    [Test]
    public async Task GenerateAndStoreInstructionAsync_ExistingMessage_SkipsGeneration()
    {
        var existing = CreateMessage("instruction", "original", "generated", 0);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(existing, Substitute.For<Response>()));

        await service.GenerateAndStoreInstructionAsync("Create a resource");

        await aiService.DidNotReceive().RephraseInstructionAsync(Arg.Any<string>());
    }

    [Test]
    public async Task GenerateAndStoreInstructionAsync_UnchangedText_DoesNotStore()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "instruction", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.RephraseInstructionAsync("Create a resource").Returns("Create a resource");

        await service.GenerateAndStoreInstructionAsync("Create a resource");

        await tableClient.DidNotReceive().UpsertEntityAsync(
            Arg.Any<PreGeneratedMessage>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateAndStoreNPCMessageAsync_NewGeneratedText_StoresCharacteristics()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.PersonalizeNPCMessageAsync("Hello", 20, "female", "engineer")
            .Returns("Personalized hello");

        var generated = await service.GenerateAndStoreNPCMessageAsync(
            "Hello",
            20,
            "female",
            "engineer");

        Assert.That(generated, Is.True);
        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message =>
                message.PartitionKey == "npc" &&
                message.OriginalMessage == "Hello" &&
                message.GeneratedMessage == "Personalized hello" &&
                message.NPCCharacteristics!.Contains("engineer")),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateAndStoreNPCMessageAsync_TransientLookupFailure_RetriesAndStores()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<NullableResponse<PreGeneratedMessage>>(new InvalidOperationException("transient")),
                Task.FromResult(missing));
        aiService.PersonalizeNPCMessageAsync("Hello", 20, "female", "engineer")
            .Returns("Personalized hello");

        await service.GenerateAndStoreNPCMessageAsync("Hello", 20, "female", "engineer");

        await tableClient.Received(2).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message =>
                message.RowKey == BuildNpcCacheKey("Hello", 20, "female", "engineer") &&
                message.GeneratedMessage == "Personalized hello"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateAndStoreNPCMessageAsync_PersistentLookupFailure_GivesUpWithoutCallingAI()
    {
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<NullableResponse<PreGeneratedMessage>>(
                new InvalidOperationException("persistent failure")));

        var generated = await service.GenerateAndStoreNPCMessageAsync(
            "Hello",
            20,
            "female",
            "engineer");

        Assert.That(generated, Is.False);
        await tableClient.Received(3).GetEntityIfExistsAsync<PreGeneratedMessage>(
            "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await aiService.DidNotReceive().PersonalizeNPCMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task GenerateAndStoreNPCMessageAsync_FallbackText_DoesNotStore()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.PersonalizeNPCMessageAsync("Hello", 20, "female", "engineer")
            .Returns("Tek, Hello");

        await service.GenerateAndStoreNPCMessageAsync("Hello", 20, "female", "engineer");

        await tableClient.DidNotReceive().UpsertEntityAsync(
            Arg.Any<PreGeneratedMessage>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshAllPreGeneratedMessagesAsync_WithNpc_ProcessesStaticMessages()
    {
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>());
        var npcTable = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("NPCCharacter").Returns(npcTable);
        npcTable.QueryAsync<NPCCharacter>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(new NPCCharacter
            {
                PartitionKey = "NPC",
                RowKey = "Stella",
                Name = "Stella",
                Age = 20,
                Gender = "female",
                Background = "engineer"
            }));
        var existing = CreateMessage("npc", "existing", "generated", 0);
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(existing, Substitute.For<Response>()));

        await service.RefreshAllPreGeneratedMessagesAsync();

        await tableClient.Received().GetEntityIfExistsAsync<PreGeneratedMessage>(
            "npc", Arg.Any<string>(), null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshAllPreGeneratedMessagesAsync_WithNpcAndTask_GeneratesTaskSpecificNpcMessages()
    {
        var missing = AzureTestResponses.Missing<PreGeneratedMessage>();
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>
        {
            CreateTask("Deploy app", "Configure App Service")
        });
        var npcTable = Substitute.For<TableClient>();
        tableServiceClient.GetTableClient("NPCCharacter").Returns(npcTable);
        npcTable.QueryAsync<NPCCharacter>(
                "PartitionKey eq 'NPC'", null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(new NPCCharacter
            {
                PartitionKey = "NPC",
                RowKey = "Stella",
                Name = "Stella",
                Age = 20,
                Gender = "female",
                Background = "engineer"
            }));
        tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>(
                Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(missing);
        aiService.RephraseInstructionAsync("Configure App Service").Returns("Rephrased task");
        aiService.PersonalizeNPCMessageAsync(Arg.Any<string>(), 20, "female", "engineer")
            .Returns(callInfo => $"{callInfo.ArgAt<string>(0)} personalized");

        await service.RefreshAllPreGeneratedMessagesAsync();

        await aiService.Received(12).PersonalizeNPCMessageAsync(
            Arg.Any<string>(), 20, "female", "engineer");
        await aiService.Received(1).PersonalizeNPCMessageAsync(
            "Ready for this? Task: Deploy app. Configure App Service", 20, "female", "engineer");
        await tableClient.Received(12).UpsertEntityAsync(
            Arg.Is<PreGeneratedMessage>(message => message.PartitionKey == "npc"),
            TableUpdateMode.Merge,
            Arg.Any<CancellationToken>());
    }

    private static PreGeneratedMessage CreateMessage(
        string type,
        string original,
        string generated,
        int hitCount)
    {
        return new PreGeneratedMessage
        {
            PartitionKey = type,
            RowKey = Guid.NewGuid().ToString(),
            OriginalMessage = original,
            GeneratedMessage = generated,
            MessageType = type,
            HitCount = hitCount,
            ETag = ETag.All
        };
    }

    private static GameTaskData CreateTask(string name, string instruction)
    {
        return new GameTaskData
        {
            Name = name,
            Tests = [],
            Instruction = instruction,
            Filter = "*",
            TimeLimit = 60,
            Reward = 100
        };
    }

    private static string BuildNpcCacheKey(string originalMessage, int age, string gender, string background)
    {
        return MessageCacheKeyHelper.CreateNpcKey(
            originalMessage,
            age,
            gender,
            background);
    }

    private static string ComputeHash(string input)
    {
        return MessageCacheKeyHelper.ComputeHash(input);
    }
}
