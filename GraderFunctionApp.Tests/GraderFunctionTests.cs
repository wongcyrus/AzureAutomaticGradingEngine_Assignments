using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class GraderFunctionTests
{
    private const string Email = "student@example.com";
    private const string Game = "azure-learning";
    private const string Npc = "Stella";

    private IStorageService storageService = null!;
    private IGameTaskService gameTaskService = null!;
    private ITestRunner testRunner = null!;
    private ITestResultParser resultParser = null!;
    private IGameStateService gameStateService = null!;
    private IUnifiedMessageService messageService = null!;
    private IRequestAuthenticator requestAuthenticator = null!;
    private GraderFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        storageService = Substitute.For<IStorageService>();
        gameTaskService = Substitute.For<IGameTaskService>();
        testRunner = Substitute.For<ITestRunner>();
        resultParser = Substitute.For<ITestResultParser>();
        gameStateService = Substitute.For<IGameStateService>();
        messageService = Substitute.For<IUnifiedMessageService>();
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns(Email);
        function = new GraderFunction(
            NullLogger<GraderFunction>.Instance,
            storageService,
            gameTaskService,
            testRunner,
            resultParser,
            gameStateService,
            messageService,
            requestAuthenticator);
    }

    [Test]
    public async Task Run_MissingSignedIdentity_ReturnsUnauthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);

        var result = await function.Run(CreateRequest(HttpMethods.Get));

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task Run_GetWithoutSubscription_ReturnsHtmlForm()
    {
        var result = await function.Run(CreateRequest(HttpMethods.Get));

        var content = result as ContentResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content?.StatusCode, Is.EqualTo(200));
            Assert.That(content?.ContentType, Is.EqualTo("text/html"));
        }
    }

    [Test]
    public async Task Run_GetWithInvalidSubscription_ReturnsBadRequest()
    {
        var result = await function.Run(CreateRequest(
            HttpMethods.Get,
            new Dictionary<string, string?> { ["subscriptionId"] = "invalid" }));

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Run_GetWithValidSubscription_ReturnsAndStoresXml()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        const string xml = "<test-run />";
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), subscriptionId, Email, "test=Test.One")
            .Returns(xml);
        resultParser.ParseNUnitTestResult(xml)
            .Returns(new Dictionary<string, int> { ["TestOne"] = 1 });
        storageService.SaveTestResultXmlAsync(Email, xml).Returns("result.xml");
        gameTaskService.GetTasksJson(false).Returns("[]");

        var result = await function.Run(CreateRequest(
            HttpMethods.Get,
            new Dictionary<string, string?>
            {
                ["subscriptionId"] = subscriptionId,
                ["filter"] = "test=Test.One",
                ["trace"] = "victim@example.com"
            }));

        var content = result as ContentResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content?.StatusCode, Is.EqualTo(200));
            Assert.That(content?.ContentType, Is.EqualTo("application/xml"));
            Assert.That(content?.Content, Is.EqualTo(xml));
        }
        await storageService.Received(1).SavePassTestRecordAsync(
            Email, "test=Test.One", Arg.Any<Dictionary<string, int>>(), "Unknown");
        await storageService.Received(1).SaveFailTestRecordAsync(
            Email, "test=Test.One", Arg.Any<Dictionary<string, int>>(), "Unknown");
    }

    [Test]
    public async Task Run_GetWhenRunnerFails_ReturnsServerError()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), subscriptionId, Arg.Any<string>(), Arg.Any<string>())
            .Returns((string?)null);

        var result = await function.Run(CreateRequest(
            HttpMethods.Get,
            new Dictionary<string, string?>
            {
                ["subscriptionId"] = subscriptionId,
                ["filter"] = "test=Test.One"
            }));

        Assert.That((result as ContentResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task Run_PostWithInvalidSubscription_ReturnsUnprocessableEntity()
    {
        var request = CreateRequest(HttpMethods.Post, new Dictionary<string, string?> { ["xml"] = "true" });
        request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["subscriptionId"] = "invalid",
            ["filter"] = "test=Test.One"
        });

        var result = await function.Run(request);

        Assert.That((result as ContentResult)?.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task Run_PostWithoutFormBody_ReturnsInternalServerError()
    {
        var result = await function.Run(CreateRequest(HttpMethods.Post));

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task Run_PostWhenRunnerProducesNoXml_ReturnsServerError()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var request = CreateRequest(HttpMethods.Post);
        request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["subscriptionId"] = subscriptionId,
            ["filter"] = "test=Test.One"
        });
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), subscriptionId, Email, "test=Test.One")
            .Returns((string?)null);

        var result = await function.Run(request);

        Assert.That((result as ContentResult)?.StatusCode, Is.EqualTo(500));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Run_PostWithValidSubscription_ReturnsRequestedFormat(bool includeXml)
    {
        var subscriptionId = Guid.NewGuid().ToString();
        const string xml = "<test-run />";
        var request = CreateRequest(
            HttpMethods.Post,
            includeXml
                ? new Dictionary<string, string?> { ["xml"] = "true" }
                : null);
        request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["subscriptionId"] = subscriptionId,
            ["filter"] = "test=Test.One"
        });
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), subscriptionId, Email, "test=Test.One")
            .Returns(xml);
        resultParser.ParseNUnitTestResult(xml)
            .Returns(new Dictionary<string, int> { ["TestOne"] = 1 });
        storageService.SaveTestResultXmlAsync(Email, xml).Returns("result.xml");
        gameTaskService.GetTasksJson(false).Returns("[]");

        var result = await function.Run(request);

        if (includeXml)
        {
            Assert.That((result as ContentResult)?.Content, Is.EqualTo(xml));
        }
        else
        {
            var response = (result as JsonResult)?.Value as ApiResponse<Dictionary<string, int>>;
            Assert.That(response?.Data?["TestOne"], Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Run_GameModeWithoutSession_ReturnsGameError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns((GameState?)null);

        var result = await function.Run(CreateRequest(
            HttpMethods.Get,
            new Dictionary<string, string?>
            {
                ["gameMode"] = "true",
                ["email"] = Email,
                ["game"] = Game,
                ["npc"] = Npc
            }));

        var body = (result as JsonResult)?.Value as GameResponse;
        Assert.That(body?.Message, Does.Contain("No active game session"));
    }

    [Test]
    public async Task Run_UnsupportedMethod_ReturnsBadRequest()
    {
        var result = await function.Run(CreateRequest(HttpMethods.Delete));

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task HandleGameGradingAsync_NoSession_ReturnsError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns((GameState?)null);

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo("ERROR"));
            Assert.That(result.Message, Does.Contain("No active game session"));
        }
    }

    [Test]
    public async Task HandleGameGradingAsync_NoActiveTask_ReturnsError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(new GameState());
        gameStateService.GetAllGameStatesForUserAsync(Email).Returns(new List<GameState>());

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        Assert.That(result.Message, Does.Contain("No active task found"));
    }

    [Test]
    public async Task HandleGameGradingAsync_TaskBelongsToOtherNpc_ReturnsWrongNpc()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(new GameState());
        gameStateService.GetAllGameStatesForUserAsync(Email).Returns(new List<GameState>
        {
            new()
            {
                RowKey = $"{Game}-Nova",
                HasActiveTask = true,
                CurrentTaskName = "Nova task"
            }
        });

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo("OK"));
            Assert.That(result.NextGamePhrase, Is.EqualTo("WRONG_NPC_FOR_GRADING"));
            Assert.That(result.Message, Is.Not.Empty);
        }
    }

    [Test]
    public async Task HandleGameGradingAsync_NoRegisteredSubscription_ReturnsError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(CreateActiveState());
        storageService.GetSubscriptionIdAsync(Email).Returns((string?)null);

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        Assert.That(result.Message, Does.Contain("No Azure subscription is registered"));
    }

    [Test]
    public async Task HandleGameGradingAsync_TestRunnerProducesNoXml_ReturnsError()
    {
        var state = CreateActiveState();
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(state);
        storageService.GetSubscriptionIdAsync(Email).Returns(Guid.NewGuid().ToString());
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), Arg.Any<string>(), Email, state.CurrentTaskFilter)
            .Returns((string?)null);

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        Assert.That(result.Message, Does.Contain("Failed to run tests"));
    }

    [Test]
    public async Task HandleGameGradingAsync_AllTestsPass_CompletesTaskAndRewardsStudent()
    {
        var state = CreateActiveState();
        ConfigureTestRun(state, new Dictionary<string, int> { ["TestOne"] = 1 });
        gameStateService.CompleteTaskAsync(
                Email, Game, Npc, state.CurrentTaskName, state.CurrentTaskReward)
            .Returns(new GameState { TotalScore = 30, CompletedTasks = 3 });
        messageService.GetTaskCompletedMessageAsync(Npc, state.CurrentTaskName, state.CurrentTaskReward)
            .Returns("Task completed");
        storageService.GetRandomEasterEggAsync("Pass").Returns("https://example.com/pass");

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo("OK"));
            Assert.That(result.NextGamePhrase, Is.EqualTo("READY_FOR_NEXT"));
            Assert.That(result.TaskCompleted, Is.True);
            Assert.That(result.TaskName, Is.EqualTo(state.CurrentTaskName));
            Assert.That(result.Score, Is.EqualTo(30));
            Assert.That(result.CompletedTasks, Is.EqualTo(3));
            Assert.That(result.EasterEggUrl, Is.EqualTo("https://example.com/pass"));
        }
    }

    [Test]
    public async Task HandleGameGradingAsync_FailedTests_KeepsTaskAndReturnsReport()
    {
        var state = CreateActiveState();
        var testResults = new Dictionary<string, int>
        {
            ["TestOne"] = 1,
            ["TestTwo"] = 0
        };
        ConfigureTestRun(state, testResults);
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData> { CreateTask(state) });
        messageService.GetTaskFailedMessageAsync(Npc, state.CurrentTaskName, 1, 2)
            .Returns("One test failed");
        messageService.GetTaskAssignedMessageAsync(Npc, state.CurrentTaskName, "Create the resource")
            .Returns("Review the requirement");
        gameStateService.TryUpdateActiveTaskMessageAsync(
                Email, Game, Npc, state.CurrentTaskName, "One test failed\n\nReview the requirement")
            .Returns(new GameState
            {
                CurrentTaskName = state.CurrentTaskName,
                LastMessage = "One test failed\n\nReview the requirement",
                TotalScore = 20,
                CompletedTasks = 2
            });
        storageService.GetRandomEasterEggAsync("Fail").Returns("https://example.com/fail");
        storageService.GenerateTestResultSasUrlAsync("result.xml").Returns("https://example.com/result.xml");

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.NextGamePhrase, Is.EqualTo("TASK_ASSIGNED"));
            Assert.That(result.TaskCompleted, Is.False);
            Assert.That(result.Message, Does.Contain("Review the requirement"));
            Assert.That(result.EasterEggUrl, Is.EqualTo("https://example.com/fail"));
            Assert.That(result.AdditionalData["passedTests"], Is.EqualTo(1));
            Assert.That(result.AdditionalData["totalTests"], Is.EqualTo(2));
            Assert.That(result.AdditionalData["testResults"], Is.SameAs(testResults));
            Assert.That(result.AdditionalData["testResultXmlUrl"], Is.EqualTo("https://example.com/result.xml"));
        }
    }

    [Test]
    public async Task HandleGameGradingAsync_TaskChangesDuringFailedRun_ReturnsError()
    {
        var state = CreateActiveState();
        ConfigureTestRun(state, new Dictionary<string, int> { ["TestOne"] = 0 });
        gameTaskService.GetTasks(false).Returns(new List<GameTaskData> { CreateTask(state) });
        messageService.GetTaskFailedMessageAsync(Npc, state.CurrentTaskName, 0, 1).Returns("Failed");
        messageService.GetTaskAssignedMessageAsync(Npc, state.CurrentTaskName, "Create the resource")
            .Returns("Requirement");
        gameStateService.TryUpdateActiveTaskMessageAsync(
                Email, Game, Npc, state.CurrentTaskName, "Failed\n\nRequirement")
            .Returns((GameState?)null);

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        Assert.That(result.Message, Does.Contain("task changed while grading"));
    }

    [Test]
    public async Task HandleGameGradingAsync_ServiceFailure_ReturnsError()
    {
        gameStateService.GetGameStateAsync(Email, Game, Npc)
            .Returns<Task<GameState?>>(_ => throw new InvalidOperationException("storage unavailable"));

        var result = await function.HandleGameGradingAsync(Email, Game, Npc);

        Assert.That(result.Message, Does.Contain("storage unavailable"));
    }

    private void ConfigureTestRun(GameState state, Dictionary<string, int> testResults)
    {
        const string xml = "<test-run />";
        gameStateService.GetGameStateAsync(Email, Game, Npc).Returns(state);
        storageService.GetSubscriptionIdAsync(Email).Returns(Guid.NewGuid().ToString());
        testRunner.RunUnitTestProcessAsync(
                Arg.Any<ILogger>(), Arg.Any<string>(), Email, state.CurrentTaskFilter)
            .Returns(xml);
        resultParser.ParseNUnitTestResult(xml).Returns(testResults);
        storageService.SaveTestResultXmlAsync(Email, xml).Returns("result.xml");
        gameTaskService.GetTasksJson(false)
            .Returns(JsonConvert.SerializeObject(new[] { CreateTask(state) }));
    }

    private static GameState CreateActiveState()
    {
        return new GameState
        {
            RowKey = $"{Game}-{Npc}",
            HasActiveTask = true,
            CurrentTaskName = "Task one",
            CurrentTaskFilter = "test=Test.One",
            CurrentTaskReward = 10,
            TotalScore = 20,
            CompletedTasks = 2
        };
    }

    private static GameTaskData CreateTask(GameState state)
    {
        return new GameTaskData
        {
            Name = state.CurrentTaskName,
            Tests = ["Namespace.TestOne", "Namespace.TestTwo"],
            Instruction = "Create the resource",
            Filter = state.CurrentTaskFilter,
            Reward = state.CurrentTaskReward,
            TimeLimit = 5
        };
    }

    private static HttpRequest CreateRequest(
        string method,
        IReadOnlyDictionary<string, string?>? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (query != null)
        {
            context.Request.QueryString = QueryString.Create(query);
        }
        return context.Request;
    }
}
