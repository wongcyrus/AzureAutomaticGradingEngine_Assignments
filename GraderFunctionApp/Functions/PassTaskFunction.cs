using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Functions
{
    public class PassTaskFunction
    {
        private readonly ILogger<PassTaskFunction> _logger;
        private readonly IStorageService _storageService;
        private readonly IGameStateService _gameStateService;
        private readonly IRequestAuthenticator _requestAuthenticator;

        public PassTaskFunction(
            ILogger<PassTaskFunction> logger,
            IStorageService storageService,
            IGameStateService gameStateService,
            IRequestAuthenticator requestAuthenticator)
        {
            _logger = logger;
            _storageService = storageService;
            _gameStateService = gameStateService;
            _requestAuthenticator = requestAuthenticator;
        }

        [Function(nameof(PassTaskFunction))]
        public async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Start PassTaskFunction");

            var email = _requestAuthenticator.GetAuthenticatedEmail(req);
            if (email == null)
            {
                return new UnauthorizedObjectResult(
                    ApiResponse.ErrorResult("Authentication required."));
            }

            if (HttpMethods.IsPost(req.Method))
            {
                if (!string.Equals(
                        req.Query["action"],
                        "reset",
                        StringComparison.Ordinal))
                {
                    return new BadRequestObjectResult(
                        ApiResponse.ErrorResult("The reset action is required."));
                }

                return await ResetGameAsync(email);
            }

            if (!HttpMethods.IsGet(req.Method))
            {
                return new ObjectResult(
                    ApiResponse.ErrorResult("Unsupported HTTP method."))
                {
                    StatusCode = StatusCodes.Status405MethodNotAllowed
                };
            }

            return await GetPlayerProgressAsync(email);
        }

        private async Task<IActionResult> GetPlayerProgressAsync(string email)
        {
            _logger.LogInformation("Fetching player progress for email: {email}", email);
            try
            {
                var passedTasksTask = _storageService.GetPassedTasksAsync(email);
                var failedTestsTask = _storageService.GetFailedTestsAsync(email);
                var subscriptionTask = _storageService.GetSubscriptionIdAsync(email);
                var gameStatesTask = _gameStateService.GetAllGameStatesForUserAsync(email);
                var taskLockTask = _gameStateService.GetActiveTaskLockAsync(email);
                await Task.WhenAll(
                    passedTasksTask,
                    failedTestsTask,
                    subscriptionTask,
                    gameStatesTask,
                    taskLockTask);

                var passedTasks = await passedTasksTask;
                var failedTests = await failedTestsTask;
                var gameStates = await gameStatesTask;
                var taskLock = await taskLockTask;
                var totalMarks = passedTasks.Sum(static task => task.Mark);
                var activeState = gameStates.FirstOrDefault(static state =>
                    state.HasActiveTask);
                var latestActivity = gameStates.Count == 0
                    ? (DateTimeOffset?)null
                    : new DateTimeOffset(gameStates.Max(static state =>
                        state.LastUpdated));

                var result = new PlayerProgressSummary
                {
                    Email = email,
                    SubscriptionId = await subscriptionTask,
                    TotalMarks = totalMarks,
                    PassedTasks = passedTasks
                        .Select(static task => new PassedTaskSummary
                        {
                            Name = task.Name,
                            Mark = task.Mark
                        })
                        .ToList(),
                    FailedAttemptCount = failedTests.Count,
                    FailedAttempts = failedTests
                        .OrderByDescending(static failure => failure.FailedAt)
                        .Take(100)
                        .Select(static failure => new FailedAttemptSummary
                        {
                            TestName = failure.TestName,
                            TaskName = failure.TaskName,
                            AssignedByNpc = failure.AssignedByNPC,
                            FailedAt = failure.FailedAt
                        })
                        .ToList(),
                    ActiveTask = activeState == null && taskLock == null
                        ? null
                        : new ActiveTaskSummary
                        {
                            Name = taskLock?.TaskName
                                ?? activeState?.CurrentTaskName
                                ?? string.Empty,
                            Npc = taskLock?.Npc ?? string.Empty,
                            Reward = activeState?.CurrentTaskReward ?? 0
                        },
                    LastActivity = latestActivity
                };

                return new JsonResult(
                    ApiResponse<PlayerProgressSummary>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to fetch player progress for email: {email}",
                    email);
                return new ObjectResult(ApiResponse.ErrorResult("Internal server error", ex.Message))
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<IActionResult> ResetGameAsync(string email)
        {
            _logger.LogInformation("Resetting game progress for email: {email}", email);
            try
            {
                await _gameStateService.BeginGameResetAsync(email);
                try
                {
                    var preservedFailures =
                        await _storageService.GetFailedTestsAsync(email);
                    var removedGameStates =
                        await _gameStateService.DeleteAllGameStatesAsync(email);
                    var removedPassedTests =
                        await _storageService.DeletePassedTasksAsync(email);

                    await Task.Delay(TimeSpan.FromSeconds(1));
                    removedGameStates +=
                        await _gameStateService.DeleteAllGameStatesAsync(email);
                    removedPassedTests +=
                        await _storageService.DeletePassedTasksAsync(email);

                    var result = new ResetGameResult
                    {
                        Email = email,
                        RemovedGameStates = removedGameStates,
                        RemovedPassedTests = removedPassedTests,
                        PreservedFailedAttempts = preservedFailures.Count
                    };
                    return new JsonResult(
                        ApiResponse<ResetGameResult>.SuccessResult(result));
                }
                finally
                {
                    await _gameStateService.EndGameResetAsync(email);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Game reset conflicted with active writes for {email}",
                    email);
                return new ObjectResult(
                    ApiResponse.ErrorResult(
                        "Game activity is still running. Close other game tabs and retry."))
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset game for {email}", email);
                return new ObjectResult(
                    ApiResponse.ErrorResult("Failed to reset game progress."))
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}
