using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class GameTaskFunctionTests
{
    private const string Email = "student@example.com";
    private const string Game = "azure-learning";
    private const string Npc = "Stella";

    private IGameTaskService gameTaskService = null!;
    private IGameStateService gameStateService = null!;
    private IStorageService storageService = null!;
    private IUnifiedMessageService messageService = null!;
    private IRequestAuthenticator requestAuthenticator = null!;
    private GameTaskFunction function = null!;
    private GameState state = null!;

    [SetUp]
    public void SetUp()
    {
        gameTaskService = Substitute.For<IGameTaskService>();
        gameStateService = Substitute.For<IGameStateService>();
        storageService = Substitute.For<IStorageService>();
        messageService = Substitute.For<IUnifiedMessageService>();
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns(Email);

        state = new GameState
        {
            PartitionKey = Email,
            RowKey = $"{Game}-{Npc}",
            LastUpdated = DateTime.UtcNow.AddHours(-2),
            TotalScore = 20,
            CompletedTasks = 2,
            CompletedTasksList = "[]"
        };

        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(state);
        gameStateService.GetActiveTaskLockAsync(Email).Returns((GameTaskLock?)null);
        gameStateService.GetAllGameStatesForUserAsync(Email).Returns(new List<GameState>());
        storageService.GetLastTaskNPCAsync(Email).Returns((string?)null);

        function = new GameTaskFunction(
            NullLogger<GameTaskFunction>.Instance,
            gameTaskService,
            gameStateService,
            storageService,
            messageService,
            requestAuthenticator);
    }

    [Test]
    public async Task Run_MissingSignedIdentity_ReturnsUnauthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);

        var result = await RunAsync();

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task Run_ExistingTask_ReturnsStoredAssignment()
    {
        state.HasActiveTask = true;
        state.CurrentTaskName = "Existing task";
        state.LastMessage = "Keep working";

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("TASK_ASSIGNED"));
            Assert.That(body.TaskName, Is.EqualTo("Existing task"));
            Assert.That(body.Message, Is.EqualTo("Keep working"));
            Assert.That(body.Score, Is.EqualTo(20));
            Assert.That(body.CompletedTasks, Is.EqualTo(2));
        }
        await gameTaskService.DidNotReceive()
            .GetNextTaskAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task Run_ActiveLockFromOtherNpc_ReturnsBusyResponse()
    {
        gameStateService.GetActiveTaskLockAsync(Email).Returns(new GameTaskLock
        {
            PartitionKey = Email,
            RowKey = "__active_task_lock__",
            Game = Game,
            Npc = "Nova",
            TaskName = "Nova task"
        });
        messageService.GetBusyWithOtherNPCMessageAsync(Npc, "Nova").Returns("Finish Nova's task");

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("BUSY_WITH_OTHER_NPC"));
            Assert.That(body.Message, Is.EqualTo("Finish Nova's task"));
            Assert.That(body.AdditionalData["activeTaskNPC"], Is.EqualTo("Nova"));
            Assert.That(body.AdditionalData["activeTaskName"], Is.EqualTo("Nova task"));
        }
    }

    [Test]
    public async Task Run_RecentTaskFromSameNpc_ReturnsCooldown()
    {
        state.LastUpdated = DateTime.UtcNow.AddMinutes(-10);
        storageService.GetLastTaskNPCAsync(Email).Returns(Npc);
        messageService.GetCooldownMessageAsync(Npc, Arg.Any<int>()).Returns("Please wait");

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("NPC_COOLDOWN"));
            Assert.That(body.Message, Is.EqualTo("Please wait"));
            Assert.That((int)body.AdditionalData["cooldownMinutes"], Is.InRange(49, 50));
            Assert.That(body.AdditionalData, Contains.Key("nextAvailableTime"));
        }
    }

    [Test]
    public async Task Run_NoAvailableTask_ReturnsAllCompleted()
    {
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns((GameTaskData?)null);
        messageService.GetAllTasksCompletedMessageAsync(Npc).Returns("Everything is complete");

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("ALL_COMPLETED"));
            Assert.That(body.Message, Is.EqualTo("Everything is complete"));
            Assert.That(body.Score, Is.EqualTo(20));
        }
    }

    [Test]
    public async Task Run_MissingState_InitializesBeforeReturningAllCompleted()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns((GameState?)null);
        gameStateService.InitializeGameStateAsync(Email, Game, Npc).Returns(state);
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns((GameTaskData?)null);
        messageService.GetAllTasksCompletedMessageAsync(Npc).Returns("Everything is complete");

        var response = await RunAsync();

        Assert.That(GetGameResponse(response).NextGamePhrase, Is.EqualTo("ALL_COMPLETED"));
        await gameStateService.Received(1).InitializeGameStateAsync(Email, Game, Npc);
    }

    [Test]
    public async Task Run_LegacyActiveStateFromOtherNpc_ReturnsBusyResponse()
    {
        gameStateService.GetAllGameStatesForUserAsync(Email).Returns(new List<GameState>
        {
            new()
            {
                RowKey = $"{Game}-Nova",
                HasActiveTask = true,
                CurrentTaskName = "Legacy Nova task"
            }
        });
        messageService.GetBusyWithOtherNPCMessageAsync(Npc, "Nova").Returns("Finish Nova's task");

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("BUSY_WITH_OTHER_NPC"));
            Assert.That(body.AdditionalData["activeTaskNPC"], Is.EqualTo("Nova"));
            Assert.That(body.AdditionalData["activeTaskName"], Is.EqualTo("Legacy Nova task"));
        }
    }

    [Test]
    public async Task Run_NewTask_AssignsTaskAndReturnsItsMetadata()
    {
        var task = CreateTask();
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns(task);
        messageService.GetTaskAssignedMessageAsync(Npc, task.Name, task.Instruction)
            .Returns("New assignment");
        gameStateService.TryAssignTaskAsync(
                Email, Game, Npc, task.Name, task.Filter, task.Reward, "New assignment")
            .Returns(state);

        var response = await RunAsync(" Student@Example.com ");

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("TASK_ASSIGNED"));
            Assert.That(body.Message, Is.EqualTo("New assignment"));
            Assert.That(body.TaskName, Is.EqualTo(task.Name));
            Assert.That(body.AdditionalData["instruction"], Is.EqualTo(task.Instruction));
            Assert.That(body.AdditionalData["timeLimit"], Is.EqualTo(task.TimeLimit));
            Assert.That(body.AdditionalData["reward"], Is.EqualTo(task.Reward));
            Assert.That(body.AdditionalData["tests"], Is.EqualTo(task.Tests));
        }
        await gameStateService.Received(1).TryAssignTaskAsync(
            Email, Game, Npc, task.Name, task.Filter, task.Reward, "New assignment");
    }

    [Test]
    public async Task Run_NextTaskAlreadyCompleted_AssignsFirstUncompletedTask()
    {
        var completedTask = CreateTask();
        var uncompletedTask = CreateTask();
        uncompletedTask.Name = "Task two";
        uncompletedTask.Filter = "test=Test.Two";
        state.CompletedTasksList = """["Task one"]""";
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns(completedTask);
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData>
        {
            completedTask,
            uncompletedTask
        });
        messageService.GetTaskAssignedMessageAsync(
                Npc, uncompletedTask.Name, uncompletedTask.Instruction)
            .Returns("Second assignment");
        gameStateService.TryAssignTaskAsync(
                Email,
                Game,
                Npc,
                uncompletedTask.Name,
                uncompletedTask.Filter,
                uncompletedTask.Reward,
                "Second assignment")
            .Returns(state);

        var response = await RunAsync();

        Assert.That(GetGameResponse(response).TaskName, Is.EqualTo("Task two"));
    }

    [Test]
    public async Task Run_ConcurrentAssignmentWonByOtherNpc_ReturnsBusyResponse()
    {
        var task = CreateTask();
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns(task);
        messageService.GetTaskAssignedMessageAsync(Npc, task.Name, task.Instruction)
            .Returns("New assignment");
        gameStateService.TryAssignTaskAsync(
                Email, Game, Npc, task.Name, task.Filter, task.Reward, "New assignment")
            .Returns((GameState?)null);
        gameStateService.GetActiveTaskLockAsync(Email).Returns(
            (GameTaskLock?)null,
            new GameTaskLock
            {
                PartitionKey = Email,
                RowKey = "__active_task_lock__",
                Game = Game,
                Npc = "Nova",
                TaskName = "Winning task"
            });
        gameStateService.GetGameStateAsync(Email, Game, "Nova").Returns(new GameState
        {
            TotalScore = 30,
            CompletedTasks = 3
        });
        messageService.GetBusyWithOtherNPCMessageAsync(Npc, "Nova").Returns("Nova won");

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("BUSY_WITH_OTHER_NPC"));
            Assert.That(body.Message, Is.EqualTo("Nova won"));
            Assert.That(body.Score, Is.EqualTo(30));
            Assert.That(body.AdditionalData["activeTaskNPC"], Is.EqualTo("Nova"));
        }
    }

    [Test]
    public async Task Run_ConcurrentAssignmentWonBySameNpc_ReturnsWinningTask()
    {
        var task = CreateTask();
        state.LastMessage = "Winning assignment";
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns(task);
        messageService.GetTaskAssignedMessageAsync(Npc, task.Name, task.Instruction)
            .Returns("New assignment");
        gameStateService.TryAssignTaskAsync(
                Email, Game, Npc, task.Name, task.Filter, task.Reward, "New assignment")
            .Returns((GameState?)null);
        gameStateService.GetActiveTaskLockAsync(Email).Returns(
            (GameTaskLock?)null,
            new GameTaskLock
            {
                PartitionKey = Email,
                Game = Game,
                Npc = Npc,
                TaskName = "Winning task"
            });

        var response = await RunAsync();

        var body = GetGameResponse(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.NextGamePhrase, Is.EqualTo("TASK_ASSIGNED"));
            Assert.That(body.TaskName, Is.EqualTo("Winning task"));
            Assert.That(body.Message, Is.EqualTo("Winning assignment"));
        }
    }

    [Test]
    public async Task Run_AssignmentLockDisappears_ReturnsInternalServerError()
    {
        var task = CreateTask();
        gameTaskService.GetNextTaskAsync(Email, Npc, Game).Returns(task);
        messageService.GetTaskAssignedMessageAsync(Npc, task.Name, task.Instruction)
            .Returns("New assignment");
        gameStateService.TryAssignTaskAsync(
                Email, Game, Npc, task.Name, task.Filter, task.Reward, "New assignment")
            .Returns((GameState?)null);

        var response = await RunAsync();

        var objectResult = response as ObjectResult;
        var body = objectResult?.Value as GameResponse;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectResult?.StatusCode, Is.EqualTo(500));
            Assert.That(body?.Message, Does.Contain("lock was lost"));
        }
    }

    [Test]
    public async Task Run_ServiceFailure_ReturnsInternalServerError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc)
            .Returns<Task<GameState?>>(_ => throw new InvalidOperationException("storage unavailable"));

        var response = await RunAsync();

        var objectResult = response as ObjectResult;
        var body = objectResult?.Value as GameResponse;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectResult?.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(body?.Status, Is.EqualTo("ERROR"));
            Assert.That(body?.Message, Does.Contain("storage unavailable"));
        }
    }

    private async Task<IActionResult> RunAsync(string email = Email)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["email"] = email,
            ["game"] = Game,
            ["npc"] = Npc
        });
        return await function.Run(context.Request);
    }

    private static GameResponse GetGameResponse(IActionResult result)
    {
        var jsonResult = result as JsonResult;
        Assert.That(jsonResult, Is.Not.Null);
        Assert.That(jsonResult!.Value, Is.TypeOf<GameResponse>());
        return (GameResponse)jsonResult.Value!;
    }

    private static GameTaskData CreateTask()
    {
        return new GameTaskData
        {
            Name = "Task one",
            Tests = ["Test.One"],
            Instruction = "Create the resource",
            Filter = "test=Test.One",
            TimeLimit = 5,
            Reward = 10
        };
    }
}
