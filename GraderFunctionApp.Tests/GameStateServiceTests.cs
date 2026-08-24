using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Models;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class GameStateServiceTests
{
    private const string Email = "student@example.com";
    private const string Game = "azure-learning";
    private const string Npc = "Stella";

    private TableClient tableClient = null!;
    private GameStateService service = null!;

    [SetUp]
    public void SetUp()
    {
        tableClient = Substitute.For<TableClient>();
        var tableServiceClient = Substitute.For<TableServiceClient>();
        tableServiceClient.GetTableClient("GameStates").Returns(tableClient);
        var missingResetMarker = AzureTestResponses.Missing<GameResetMarker>();
        tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Arg.Any<string>(),
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(missingResetMarker);
        service = new GameStateService(
            tableServiceClient,
            NullLogger<GameStateService>.Instance);
    }

    [Test]
    public async Task GetGameStateAsync_ExistingEntity_ReturnsState()
    {
        var state = CreateState();
        ReturnEntity(state);

        var result = await service.GetGameStateAsync(Email, Game, Npc);

        Assert.That(result, Is.SameAs(state));
    }

    [Test]
    public async Task GetGameStateAsync_ClientFailure_ReturnsNull()
    {
        tableClient.GetEntityIfExistsAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns<Task<NullableResponse<GameState>>>(_ => throw new InvalidOperationException());

        var result = await service.GetGameStateAsync(Email, Game, Npc);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreateOrUpdateGameStateAsync_UpdatesTimestampAndPersists()
    {
        var state = CreateState();
        var before = DateTime.UtcNow;

        var result = await service.CreateOrUpdateGameStateAsync(state);

        Assert.That(result.LastUpdated, Is.GreaterThanOrEqualTo(before));
        await tableClient.Received(1).UpsertEntityAsync(
            state, TableUpdateMode.Merge, Arg.Any<CancellationToken>());
    }

    [Test]
    public void CreateOrUpdateGameStateAsync_ClientFailure_Rethrows()
    {
        tableClient.UpsertEntityAsync(
                Arg.Any<GameState>(), TableUpdateMode.Merge, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new InvalidOperationException("write failed"));

        Func<Task> action = async () =>
            await service.CreateOrUpdateGameStateAsync(CreateState());

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task InitializeGameStateAsync_CreatesReadyState()
    {
        var result = await service.InitializeGameStateAsync(Email, Game, Npc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.PartitionKey, Is.EqualTo(Email));
            Assert.That(result.RowKey, Is.EqualTo($"{Game}-{Npc}"));
            Assert.That(result.CurrentPhase, Is.EqualTo("READY_FOR_NEXT"));
            Assert.That(result.HasActiveTask, Is.False);
            Assert.That(result.CompletedTasksList, Is.EqualTo("[]"));
        }
        await tableClient.Received(1).AddEntityAsync(result, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeGameStateAsync_ConcurrentCreation_ReturnsCreatedState()
    {
        var concurrentState = CreateState();
        tableClient.AddEntityAsync(
            Arg.Any<GameState>(), Arg.Any<CancellationToken>())
        .Returns<Task<Response>>(_ => throw new RequestFailedException(409, "Conflict"));
        ReturnEntity(concurrentState);

        var result = await service.InitializeGameStateAsync(Email, Game, Npc);

        Assert.That(result, Is.SameAs(concurrentState));
    }

    [Test]
    public void InitializeGameStateAsync_ConcurrentCreationCannotBeRead_Throws()
    {
        tableClient.AddEntityAsync(
                Arg.Any<GameState>(), Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(409, "Conflict"));
        ReturnMissingState();
        Func<Task> action = async () =>
            await service.InitializeGameStateAsync(Email, Game, Npc);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task UpdateGamePhaseAsync_ChangesPhaseAndMessage()
    {
        var state = CreateState();
        ReturnEntity(state);

        var result = await service.UpdateGamePhaseAsync(Email, Game, Npc, "READY_FOR_NEXT", "Ready");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CurrentPhase, Is.EqualTo("READY_FOR_NEXT"));
            Assert.That(result.LastMessage, Is.EqualTo("Ready"));
        }
    }

    [Test]
    public async Task GetActiveTaskLockAsync_ExistingLock_ReturnsLock()
    {
        var taskLock = new GameTaskLock { PartitionKey = Email, Game = Game, Npc = Npc };
        tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                Email, GameTaskLock.LockRowKey, null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(taskLock, Substitute.For<Response>()));

        var result = await service.GetActiveTaskLockAsync(Email);

        Assert.That(result, Is.SameAs(taskLock));
    }

    [Test]
    public async Task DeleteAllGameStatesAsync_DeletesStatesAndLock()
    {
        var state = new TableEntity(Email, $"{Game}-{Npc}");
        var taskLock = new TableEntity(Email, GameTaskLock.LockRowKey);
        tableClient.QueryAsync<TableEntity>(
                Arg.Any<string>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(
                AzureTestResponses.AsyncPageable(state, taskLock),
                AzureTestResponses.AsyncPageable<TableEntity>());

        var result = await service.DeleteAllGameStatesAsync(Email);

        Assert.That(result, Is.EqualTo(2));
        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                actions.Count() == 2 &&
                actions.All(action =>
                    action.ActionType == TableTransactionActionType.Delete)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BeginAndEndGameResetAsync_ManageResetMarker()
    {
        await service.BeginGameResetAsync(Email);
        await service.EndGameResetAsync(Email);

        await tableClient.Received(1).AddEntityAsync(
            Arg.Is<GameResetMarker>(marker =>
                marker.PartitionKey == Email &&
                marker.RowKey == GameResetMarker.ResetRowKey),
            Arg.Any<CancellationToken>());
        await tableClient.Received(1).DeleteEntityAsync(
            Email,
            GameResetMarker.ResetRowKey,
            ETag.All,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BeginGameResetAsync_StaleMarker_ReplacesMarker()
    {
        var attempts = 0;
        tableClient.AddEntityAsync(
                Arg.Any<GameResetMarker>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (attempts++ == 0)
                {
                    throw new RequestFailedException(409, "Conflict");
                }

                return Task.FromResult(Substitute.For<Response>());
            });
        var staleMarker = new GameResetMarker
        {
            PartitionKey = Email,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ETag = new ETag("stale")
        };
        tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Email,
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                staleMarker,
                Substitute.For<Response>()));

        await service.BeginGameResetAsync(Email);

        await tableClient.Received(1).DeleteEntityAsync(
            Email,
            GameResetMarker.ResetRowKey,
            staleMarker.ETag,
            Arg.Any<CancellationToken>());
        await tableClient.Received(2).AddEntityAsync(
            Arg.Any<GameResetMarker>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void BeginGameResetAsync_RecentMarker_Throws()
    {
        tableClient.AddEntityAsync(
                Arg.Any<GameResetMarker>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(
                _ => throw new RequestFailedException(409, "Conflict"));
        tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Email,
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                new GameResetMarker
                {
                    PartitionKey = Email,
                    StartedAt = DateTimeOffset.UtcNow
                },
                Substitute.For<Response>()));

        Func<Task> action = async () =>
            await service.BeginGameResetAsync(Email);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public void BeginGameResetAsync_ConcurrentStaleMarkerReplacement_Throws()
    {
        tableClient.AddEntityAsync(
                Arg.Any<GameResetMarker>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(
                _ => throw new RequestFailedException(409, "Conflict"));
        tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Email,
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                new GameResetMarker
                {
                    PartitionKey = Email,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                },
                Substitute.For<Response>()));

        Func<Task> action = async () =>
            await service.BeginGameResetAsync(Email);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task EndGameResetAsync_MissingMarker_IsIdempotent()
    {
        tableClient.DeleteEntityAsync(
                Email,
                GameResetMarker.ResetRowKey,
                ETag.All,
                Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(
                _ => throw new RequestFailedException(404, "Not found"));

        await service.EndGameResetAsync(Email);
    }

    [Test]
    public async Task DeleteAllGameStatesAsync_IgnoresResetMarker()
    {
        var marker = new TableEntity(Email, GameResetMarker.ResetRowKey);
        tableClient.QueryAsync<TableEntity>(
                Arg.Any<string>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(marker));

        var result = await service.DeleteAllGameStatesAsync(Email);

        Assert.That(result, Is.Zero);
        await tableClient.DidNotReceiveWithAnyArgs().SubmitTransactionAsync(
            default(IEnumerable<TableTransactionAction>)!,
            default);
    }

    [Test]
    public void CreateOrUpdateGameStateAsync_DuringReset_Throws()
    {
        tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                Email,
                GameResetMarker.ResetRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                new GameResetMarker { PartitionKey = Email },
                Substitute.For<Response>()));

        Func<Task> action = async () =>
            await service.CreateOrUpdateGameStateAsync(CreateState());

        Assert.ThrowsAsync<InvalidOperationException>(action);
        tableClient.DidNotReceiveWithAnyArgs().UpsertEntityAsync(
            default(GameState)!,
            default,
            default);
    }

    [Test]
    public async Task TryAssignTaskAsync_AvailableLock_AssignsStateAtomically()
    {
        var state = CreateState();
        ReturnEntity(state);

        var result = await service.TryAssignTaskAsync(
            Email, Game, Npc, "Task A", "test=Task.A", 10, "Do task A");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(state));
            Assert.That(result!.HasActiveTask, Is.True);
            Assert.That(result.CurrentTaskName, Is.EqualTo("Task A"));
            Assert.That(result.CurrentTaskFilter, Is.EqualTo("test=Task.A"));
            Assert.That(result.CurrentTaskReward, Is.EqualTo(10));
            Assert.That(result.LastMessage, Is.EqualTo("Do task A"));
        }
        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions => actions.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryAssignTaskAsync_ConflictingLock_ReturnsNull()
    {
        ReturnEntity(CreateState());
        tableClient.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response<IReadOnlyList<Response>>>>(
                _ => throw new RequestFailedException(409, "Conflict"));

        var result = await service.TryAssignTaskAsync(
            Email, Game, Npc, "Task A", "test=Task.A", 10, "Do task A");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CompleteTaskAsync_WithoutLock_CompletesAndRewardsTask()
    {
        var state = CreateState();
        state.HasActiveTask = true;
        state.CurrentTaskName = "Task A";
        state.CurrentTaskFilter = "test=Task.A";
        state.CurrentTaskReward = 10;
        state.CompletedTasksList = "[]";
        ReturnEntity(state);
        ReturnMissingLock();

        var result = await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasActiveTask, Is.False);
            Assert.That(result.CurrentPhase, Is.EqualTo("READY_FOR_NEXT"));
            Assert.That(result.CurrentTaskName, Is.Empty);
            Assert.That(result.CompletedTasks, Is.EqualTo(1));
            Assert.That(result.TotalScore, Is.EqualTo(10));
            Assert.That(result.CompletedTasksList, Does.Contain("Task A"));
        }
        await tableClient.Received(1).UpdateEntityAsync(
            state, state.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteTaskAsync_WithMatchingLock_UpdatesStateAndDeletesLockAtomically()
    {
        var state = CreateState();
        state.HasActiveTask = true;
        state.CurrentTaskName = "Task A";
        state.CurrentTaskReward = 10;
        ReturnEntity(state);
        var taskLock = new GameTaskLock
        {
            PartitionKey = Email,
            Game = Game,
            Npc = Npc,
            TaskName = "Task A",
            ETag = ETag.All
        };
        tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                Email, GameTaskLock.LockRowKey, null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(taskLock, Substitute.For<Response>()));

        var result = await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.That(result.CompletedTasks, Is.EqualTo(1));
        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                actions.Count() == 2 &&
                actions.Any(action => action.ActionType == TableTransactionActionType.UpdateReplace) &&
                actions.Any(action => action.ActionType == TableTransactionActionType.Delete)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void CompleteTaskAsync_LockOwnedByOtherNpc_Throws()
    {
        var state = CreateState();
        state.HasActiveTask = true;
        state.CurrentTaskName = "Task A";
        ReturnEntity(state);
        var taskLock = new GameTaskLock
        {
            PartitionKey = Email,
            Game = Game,
            Npc = "Nova",
            TaskName = "Nova task"
        };
        tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                Email, GameTaskLock.LockRowKey, null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(taskLock, Substitute.For<Response>()));
        Func<Task> action = async () =>
            await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(action);

        Assert.That(exception?.Message, Does.Contain("another task owns"));
    }

    [Test]
    public async Task CompleteTaskAsync_AlreadyCompleted_ReturnsCurrentState()
    {
        var state = CreateState();
        state.CompletedTasksList = """["Task A"]""";
        ReturnEntity(state);

        var result = await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.That(result, Is.SameAs(state));
        await tableClient.DidNotReceive().UpdateEntityAsync(
            Arg.Any<GameState>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void CompleteTaskAsync_ActiveTaskChanged_Throws()
    {
        ReturnEntity(CreateState());

        Func<Task> action = async () =>
            await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public void CompleteTaskAsync_MissingState_Throws()
    {
        ReturnMissingState();
        Func<Task> action = async () =>
            await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task CompleteTaskAsync_ConcurrentCompletion_ReturnsLatestCompletedState()
    {
        var initial = CreateState();
        initial.HasActiveTask = true;
        initial.CurrentTaskName = "Task A";
        var latest = CreateState();
        latest.CompletedTasksList = """["Task A"]""";
        ReturnEntitySequence(initial, latest);
        ReturnMissingLock();
        tableClient.UpdateEntityAsync(
                Arg.Any<GameState>(), Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(412, "Changed"));

        var result = await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.That(result, Is.SameAs(latest));
    }

    [Test]
    public void CompleteTaskAsync_ConcurrentDifferentChange_Throws()
    {
        var initial = CreateState();
        initial.HasActiveTask = true;
        initial.CurrentTaskName = "Task A";
        ReturnEntitySequence(initial, CreateState());
        ReturnMissingLock();
        tableClient.UpdateEntityAsync(
                Arg.Any<GameState>(), Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(412, "Changed"));
        Func<Task> action = async () =>
            await service.CompleteTaskAsync(Email, Game, Npc, "Task A", 10);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Test]
    public async Task TryUpdateActiveTaskMessageAsync_ActiveTask_UpdatesMessage()
    {
        var state = CreateState();
        state.HasActiveTask = true;
        state.CurrentTaskName = "Task A";
        ReturnEntity(state);

        var result = await service.TryUpdateActiveTaskMessageAsync(
            Email, Game, Npc, "Task A", "Updated");

        Assert.That(result?.LastMessage, Is.EqualTo("Updated"));
        await tableClient.Received(1).UpdateEntityAsync(
            state, state.ETag, TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryUpdateActiveTaskMessageAsync_StaleTask_ReturnsNull()
    {
        ReturnEntity(CreateState());

        var result = await service.TryUpdateActiveTaskMessageAsync(
            Email, Game, Npc, "Task A", "Updated");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryUpdateActiveTaskMessageAsync_ConcurrentWrite_ReturnsLatestMatchingState()
    {
        var initial = CreateState();
        initial.HasActiveTask = true;
        initial.CurrentTaskName = "Task A";
        var latest = CreateState();
        latest.HasActiveTask = true;
        latest.CurrentTaskName = "Task A";
        latest.LastMessage = "Written concurrently";
        ReturnEntitySequence(initial, latest);
        tableClient.UpdateEntityAsync(
                Arg.Any<GameState>(), Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(412, "Changed"));

        var result = await service.TryUpdateActiveTaskMessageAsync(
            Email, Game, Npc, "Task A", "Updated");

        Assert.That(result, Is.SameAs(latest));
    }

    [Test]
    public async Task GetAllGameStatesForUserAsync_ExcludesTaskLock()
    {
        var state = CreateState();
        tableClient.QueryAsync<GameState>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns(AzureTestResponses.AsyncPageable(
                state,
                new GameState { PartitionKey = Email, RowKey = GameTaskLock.LockRowKey }));

        var result = await service.GetAllGameStatesForUserAsync(Email);

        Assert.That(result, Is.EqualTo(new[] { state }));
    }

    [Test]
    public async Task GetAllGameStatesForUserAsync_QueryFailure_ReturnsEmpty()
    {
        tableClient.QueryAsync<GameState>(
                Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns<AsyncPageable<GameState>>(_ => throw new InvalidOperationException("query failed"));

        Assert.That(await service.GetAllGameStatesForUserAsync(Email), Is.Empty);
    }

    [Test]
    public async Task DeleteGameStateAsync_WithoutLock_DeletesStateConditionally()
    {
        var state = CreateState();
        tableClient.GetEntityAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(state, Substitute.For<Response>()));
        ReturnMissingLock();

        await service.DeleteGameStateAsync(Email, Game, Npc);

        await tableClient.Received(1).DeleteEntityAsync(
            Email, $"{Game}-{Npc}", state.ETag, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteGameStateAsync_WithMatchingLock_DeletesBothAtomically()
    {
        var state = CreateState();
        tableClient.GetEntityAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(state, Substitute.For<Response>()));
        var taskLock = new GameTaskLock
        {
            PartitionKey = Email,
            Game = Game,
            Npc = Npc,
            ETag = ETag.All
        };
        tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                Email, GameTaskLock.LockRowKey, null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(taskLock, Substitute.For<Response>()));

        await service.DeleteGameStateAsync(Email, Game, Npc);

        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                actions.Count() == 2 &&
                actions.All(action => action.ActionType == TableTransactionActionType.Delete)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void DeleteGameStateAsync_ClientFailure_Rethrows()
    {
        tableClient.GetEntityAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns<Task<Response<GameState>>>(_ => throw new InvalidOperationException("delete failed"));
        Func<Task> action = async () =>
            await service.DeleteGameStateAsync(Email, Game, Npc);

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    private void ReturnEntity(GameState state)
    {
        tableClient.GetEntityIfExistsAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(state, Substitute.For<Response>()));
    }

    private void ReturnEntitySequence(GameState first, GameState second)
    {
        tableClient.GetEntityIfExistsAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns(
                Response.FromValue(first, Substitute.For<Response>()),
                Response.FromValue(second, Substitute.For<Response>()));
    }

    private void ReturnMissingState()
    {
        var response = AzureTestResponses.Missing<GameState>();
        tableClient.GetEntityIfExistsAsync<GameState>(
                Email, $"{Game}-{Npc}", null, Arg.Any<CancellationToken>())
            .Returns(response);
    }

    private void ReturnMissingLock()
    {
        var response = Substitute.For<NullableResponse<GameTaskLock>>();
        response.HasValue.Returns(false);
        tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                Email, GameTaskLock.LockRowKey, null, Arg.Any<CancellationToken>())
            .Returns(response);
    }

    private static GameState CreateState()
    {
        return new GameState
        {
            PartitionKey = Email,
            RowKey = $"{Game}-{Npc}",
            LastUpdated = DateTime.UtcNow.AddMinutes(-1),
            CompletedTasksList = "[]"
        };
    }
}
