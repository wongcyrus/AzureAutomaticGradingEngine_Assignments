using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class PassTaskFunctionTests
{
    private IStorageService storageService = null!;
    private IGameStateService gameStateService = null!;
    private IRequestAuthenticator requestAuthenticator = null!;
    private PassTaskFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        storageService = Substitute.For<IStorageService>();
        gameStateService = Substitute.For<IGameStateService>();
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("student@example.com");
        function = new PassTaskFunction(
            NullLogger<PassTaskFunction>.Instance,
            storageService,
            gameStateService,
            requestAuthenticator);
    }

    [Test]
    public async Task Run_MissingAuthentication_ReturnsUnauthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);

        var result = await function.Run(CreateRequest());

        var response = result as UnauthorizedObjectResult;
        var body = response?.Value as ApiResponse;
        Assert.That(body?.Error, Is.EqualTo("Authentication required."));
    }

    [Test]
    public async Task Run_ReturnsNormalizedStudentsMarks()
    {
        storageService.GetPassedTasksAsync("student@example.com").Returns(new List<(string, int)>
        {
            ("Task A", 10),
            ("Task B", 20)
        });
        storageService.GetFailedTestsAsync("student@example.com")
            .Returns([]);
        gameStateService.GetAllGameStatesForUserAsync("student@example.com")
            .Returns([]);
        var request = CreateRequest();

        var result = await function.Run(request);

        var json = result as JsonResult;
        var body = json?.Value as ApiResponse<PlayerProgressSummary>;
        Assert.That(body?.Success, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body?.Data?.Email, Is.EqualTo("student@example.com"));
            Assert.That(body?.Data?.TotalMarks, Is.EqualTo(30));
            Assert.That(body?.Data?.PassedTasks, Has.Count.EqualTo(2));
        }
        await storageService.Received(1).GetPassedTasksAsync("student@example.com");
    }

    [Test]
    public async Task Run_StorageFailure_ReturnsInternalServerError()
    {
        storageService.GetPassedTasksAsync("student@example.com")
            .Returns<Task<List<(string Name, int Mark)>>>(_ => throw new InvalidOperationException("storage unavailable"));

        var result = await function.Run(CreateRequest());

        var response = result as ObjectResult;
        var body = response?.Value as ApiResponse;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response?.StatusCode, Is.EqualTo(500));
            Assert.That(body?.Success, Is.False);
            Assert.That(body?.Details, Is.EqualTo("storage unavailable"));
        }
    }

    [Test]
    public async Task Run_ProfileIncludesSubscriptionFailuresAndActiveTask()
    {
        var failedAt = DateTimeOffset.UtcNow;
        storageService.GetPassedTasksAsync("student@example.com")
            .Returns([]);
        storageService.GetFailedTestsAsync("student@example.com")
            .Returns([
                new FailTestEntity
                {
                    TestName = "Test01",
                    TaskName = "Task A",
                    AssignedByNPC = "Stella",
                    FailedAt = failedAt
                }
            ]);
        storageService.GetSubscriptionIdAsync("student@example.com")
            .Returns("11111111-1111-1111-1111-111111111111");
        gameStateService.GetAllGameStatesForUserAsync("student@example.com")
            .Returns([
                new GameState
                {
                    HasActiveTask = true,
                    CurrentTaskName = "Task A",
                    CurrentTaskReward = 10,
                    LastUpdated = DateTime.UtcNow
                }
            ]);
        gameStateService.GetActiveTaskLockAsync("student@example.com")
            .Returns(new GameTaskLock
            {
                TaskName = "Task A",
                Npc = "Stella"
            });

        var result = await function.Run(CreateRequest());

        var body = (result as JsonResult)?.Value
            as ApiResponse<PlayerProgressSummary>;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body?.Data?.SubscriptionId, Is.EqualTo(
                "11111111-1111-1111-1111-111111111111"));
            Assert.That(body?.Data?.FailedAttemptCount, Is.EqualTo(1));
            Assert.That(body?.Data?.FailedAttempts[0].FailedAt, Is.EqualTo(failedAt));
            Assert.That(body?.Data?.ActiveTask?.Npc, Is.EqualTo("Stella"));
        }
    }

    [Test]
    public async Task Run_ProfileUsesTaskLockWhenStateIsMissing()
    {
        storageService.GetPassedTasksAsync("student@example.com")
            .Returns([]);
        storageService.GetFailedTestsAsync("student@example.com")
            .Returns([]);
        gameStateService.GetAllGameStatesForUserAsync("student@example.com")
            .Returns([]);
        gameStateService.GetActiveTaskLockAsync("student@example.com")
            .Returns(new GameTaskLock
            {
                TaskName = "Locked Task",
                Npc = "Lila"
            });

        var result = await function.Run(CreateRequest());

        var body = (result as JsonResult)?.Value
            as ApiResponse<PlayerProgressSummary>;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body?.Data?.ActiveTask?.Name, Is.EqualTo("Locked Task"));
            Assert.That(body?.Data?.ActiveTask?.Npc, Is.EqualTo("Lila"));
            Assert.That(body?.Data?.ActiveTask?.Reward, Is.Zero);
            Assert.That(body?.Data?.LastActivity, Is.Null);
        }
    }

    [Test]
    public async Task Run_ResetDeletesProgressAndPreservesFailures()
    {
        gameStateService.DeleteAllGameStatesAsync("student@example.com")
            .Returns(3, 0);
        storageService.DeletePassedTasksAsync("student@example.com")
            .Returns(4, 0);
        storageService.GetFailedTestsAsync("student@example.com")
            .Returns([new FailTestEntity(), new FailTestEntity()]);

        var result = await function.Run(CreateResetRequest());

        var body = (result as JsonResult)?.Value
            as ApiResponse<ResetGameResult>;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body?.Success, Is.True);
            Assert.That(body?.Data?.RemovedGameStates, Is.EqualTo(3));
            Assert.That(body?.Data?.RemovedPassedTests, Is.EqualTo(4));
            Assert.That(body?.Data?.PreservedFailedAttempts, Is.EqualTo(2));
        }
        await storageService.DidNotReceiveWithAnyArgs()
            .SaveFailTestRecordAsync(default!, default!, default!, default!);
        await gameStateService.Received(1)
            .BeginGameResetAsync("student@example.com");
        await gameStateService.Received(1)
            .EndGameResetAsync("student@example.com");
    }

    [Test]
    public async Task Run_ResetWithoutAction_ReturnsBadRequest()
    {
        var request = CreateRequest();
        request.Method = HttpMethods.Post;

        var result = await function.Run(request);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        await gameStateService.DidNotReceiveWithAnyArgs()
            .DeleteAllGameStatesAsync(default!);
    }

    [Test]
    public async Task Run_ResetConflict_ReturnsConflict()
    {
        gameStateService.DeleteAllGameStatesAsync("student@example.com")
            .Returns<Task<int>>(_ => throw new InvalidOperationException());

        var result = await function.Run(CreateResetRequest());

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task Run_ResetUnexpectedFailure_ReturnsInternalServerError()
    {
        gameStateService.BeginGameResetAsync("student@example.com")
            .Returns<Task>(_ => throw new ApplicationException("storage down"));

        var result = await function.Run(CreateResetRequest());

        var response = result as ObjectResult;
        var body = response?.Value as ApiResponse;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response?.StatusCode, Is.EqualTo(500));
            Assert.That(body?.Error, Is.EqualTo("Failed to reset game progress."));
        }
    }

    [Test]
    public async Task Run_ResetFailureAfterMarkerAlwaysEndsReset()
    {
        storageService.GetFailedTestsAsync("student@example.com")
            .Returns<Task<List<FailTestEntity>>>(
                _ => throw new ApplicationException("read failed"));

        var result = await function.Run(CreateResetRequest());

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(500));
        await gameStateService.Received(1)
            .EndGameResetAsync("student@example.com");
    }

    [Test]
    public async Task Run_UnsupportedMethod_ReturnsMethodNotAllowed()
    {
        var request = CreateRequest();
        request.Method = HttpMethods.Delete;

        var result = await function.Run(request);

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(405));
    }

    private static HttpRequest CreateRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        return context.Request;
    }

    private static HttpRequest CreateResetRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.QueryString = QueryString.Create("action", "reset");
        return context.Request;
    }
}
