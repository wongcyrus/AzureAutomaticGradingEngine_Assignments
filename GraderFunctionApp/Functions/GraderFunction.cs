using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Constants;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Functions
{
    public class GraderFunction
    {
        private readonly ILogger<GraderFunction> _logger;
        private readonly IStorageService _storageService;
        private readonly IGameTaskService _gameTaskService;
        private readonly ITestRunner _testRunner;
        private readonly ITestResultParser _testResultParser;
        private readonly IGameStateService _gameStateService;
        private readonly IUnifiedMessageService _unifiedMessageService;

        public GraderFunction(
            ILogger<GraderFunction> logger,
            IStorageService storageService,
            IGameTaskService gameTaskService,
            ITestRunner testRunner,
            ITestResultParser testResultParser,
            IGameStateService gameStateService,
            IUnifiedMessageService unifiedMessageService)
        {
            _logger = logger;
            _storageService = storageService;
            _gameTaskService = gameTaskService;
            _testRunner = testRunner;
            _testResultParser = testResultParser;
            _gameStateService = gameStateService;
            _unifiedMessageService = unifiedMessageService;
        }

        [Function(nameof(GraderFunction))]
        public async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Start AzureGraderFunction");

            try
            {
                return req.Method switch
                {
                    "GET" => await HandleGetRequestAsync(req),
                    "POST" => await HandlePostRequestAsync(req),
                    _ => new BadRequestObjectResult(ApiResponse.ErrorResult("Unsupported HTTP method"))
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GraderFunction");
                return new ObjectResult(ApiResponse.ErrorResult("Internal server error", ex.Message))
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<IActionResult> HandleGetRequestAsync(HttpRequest req)
        {
            // Check if this is a game mode request
            if (req.Query.ContainsKey("gameMode") && req.Query["gameMode"] == "true")
            {
                return await HandleGameModeRequestAsync(req);
            }

            if (!req.Query.ContainsKey("credentials"))
            {
                return new ContentResult
                {
                    Content = HtmlTemplates.GraderForm,
                    ContentType = "text/html",
                    StatusCode = 200,
                };
            }

            string credentials = req.Query["credentials"]!;
            string filter = req.Query["filter"]!;
            string taskName = filter;
            string email = "Anonymous";

            _logger.LogInformation("GET Request - filter: '{filter}', taskName: '{taskName}'", filter, taskName);

            string? xml;
            if (req.Query.ContainsKey("trace"))
            {
                string trace = req.Query["trace"]!;
                email = UtilityHelpers.ExtractEmail(trace);
                _logger.LogInformation("start:" + trace);
                xml = await _testRunner.RunUnitTestProcessAsync(_logger, credentials, email, filter);
                _logger.LogInformation("end:" + trace);
            }
            else
            {
                xml = await _testRunner.RunUnitTestProcessAsync(_logger, credentials, email, filter);
            }

            if (string.IsNullOrEmpty(xml))
            {
                return new ContentResult 
                { 
                    Content = "<error>Failed to run tests or no results produced.</error>", 
                    ContentType = "application/xml", 
                    StatusCode = 500 
                };
            }

            await SaveTestResultsToStorageAsync(email, taskName, xml);
            return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
        }

        private async Task<IActionResult> HandlePostRequestAsync(HttpRequest req)
        {
            _logger.LogInformation("POST Request");
            
            string needXml = req.Query["xml"]!;
            string credentials = req.Form["credentials"]!;
            string filter = req.Form["filter"]!;
            string taskName = filter;

            _logger.LogInformation("POST Request - filter: '{filter}', taskName: '{taskName}'", filter, taskName);

            if (string.IsNullOrWhiteSpace(credentials))
            {
                return new ContentResult
                {
                    Content = "<result><value>No credentials</value></result>",
                    ContentType = "application/xml",
                    StatusCode = 422
                };
            }

            var xml = await _testRunner.RunUnitTestProcessAsync(_logger, credentials, "Anonymous", filter);
            if (string.IsNullOrEmpty(xml))
            {
                return new ContentResult 
                { 
                    Content = "<error>Failed to run tests or no results produced.</error>", 
                    ContentType = "application/xml", 
                    StatusCode = 500 
                };
            }

            await SaveTestResultsToStorageAsync("Anonymous", taskName, xml);

            if (string.IsNullOrEmpty(needXml))
            {
                var result = _testResultParser.ParseNUnitTestResult(xml!);
                return new JsonResult(ApiResponse<Dictionary<string, int>>.SuccessResult(result));
            }
            
            return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
        }

        private async Task<IActionResult> HandleGameModeRequestAsync(HttpRequest req)
        {
            try
            {
                var email = req.Query["email"].FirstOrDefault() ?? "unknown";
                var game = req.Query["game"].FirstOrDefault() ?? "unknown";
                var npc = req.Query["npc"].FirstOrDefault() ?? "unknown";

                _logger.LogInformation("Game mode request: {email}, {game}, {npc}", email, game, npc);

                // Credentials will be retrieved from storage in HandleGameGradingAsync
                var gameResponse = await HandleGameGradingAsync(email, game, npc, "", "");
                return new JsonResult(gameResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleGameModeRequestAsync");
                return new ObjectResult(GameResponse.Error("Internal server error: " + ex.Message))
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<string> SaveTestResultsToStorageAsync(string email, string taskName, string xml, string npc = "")
        {
            try
            {
                _logger.LogInformation("SaveTestResultsToStorage called with email: {email}, NPC: {npc}", email, npc);
                
                var testResults = _testResultParser.ParseNUnitTestResult(xml);
                var blobName = await _storageService.SaveTestResultXmlAsync(email, xml);

                // Load all tasks and their rewards
                var tasksJson = _gameTaskService.GetTasksJson(false);
                var allTasks = Newtonsoft.Json.JsonConvert.DeserializeObject<List<GameTaskData>>(tasksJson);
                
                _logger.LogInformation("Building reward map from {taskCount} tasks", allTasks?.Count ?? 0);
                
                // Build reward map: distribute task reward among its tests
                var rewardMap = new Dictionary<string, int>();
                foreach (var task in allTasks ?? new List<GameTaskData>())
                {
                    if (task.Tests?.Length > 0)
                    {
                        var rewardPerTest = task.Reward / task.Tests.Length;
                        _logger.LogInformation("Task '{taskName}' has {testCount} tests, {totalReward} total reward, {rewardPerTest} per test", 
                            task.Name, task.Tests.Length, task.Reward, rewardPerTest);
                        
                        foreach (var test in task.Tests)
                        {
                            // Extract short test name (last part after the last dot)
                            var shortTestName = test.Split('.').LastOrDefault() ?? test;
                            rewardMap[shortTestName] = rewardPerTest;
                            _logger.LogDebug("Mapped test '{testName}' (short: '{shortName}') to {reward} points", test, shortTestName, rewardPerTest);
                        }
                    }
                }
                
                _logger.LogInformation("Test results keys: {testKeys}", string.Join(", ", testResults.Keys));
                _logger.LogInformation("Reward map keys: {rewardKeys}", string.Join(", ", rewardMap.Keys));

                // Build pass dictionary with mark
                var passDict = testResults.Where(kv => kv.Value == 1)
                    .ToDictionary(kv => kv.Key, kv => {
                        var reward = rewardMap.ContainsKey(kv.Key) ? rewardMap[kv.Key] : 0;
                        _logger.LogInformation("Test '{testName}' passed, assigning {reward} points", kv.Key, reward);
                        return reward;
                    });

                _logger.LogInformation("About to save {passCount} passed tests with total marks: {totalMarks}", 
                    passDict.Count, passDict.Values.Sum());
                
                foreach (var kvp in passDict)
                {
                    _logger.LogInformation("Passed test: '{testName}' = {mark} points", kvp.Key, kvp.Value);
                }

                if (!string.IsNullOrEmpty(npc))
                {
                    await _storageService.SavePassTestRecordAsync(email, taskName, passDict, npc);
                }
                else
                {
                    await _storageService.SavePassTestRecordAsync(email, taskName, passDict, "Unknown");
                }
                
                if (!string.IsNullOrEmpty(npc))
                {
                    await _storageService.SaveFailTestRecordAsync(email, taskName, testResults, npc);
                }
                else
                {
                    await _storageService.SaveFailTestRecordAsync(email, taskName, testResults, "Unknown");
                }
                
                _logger.LogInformation("Test results saved to storage for email: {email}, NPC: {npc}", email, npc);
                return blobName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test results to storage for email: {email}", email);
                return "";
            }
        }

        public async Task<GameResponse> HandleGameGradingAsync(string email, string game, string npc, string phrase, string credentials)
        {
            try
            {
                _logger.LogInformation("HandleGameGradingAsync called for {email}, {game}, {npc}", email, game, npc);

                // Get current game state for this NPC
                var gameState = await _gameStateService.GetGameStateAsync(email, game, npc);
                if (gameState == null)
                {
                    return GameResponse.Error("No active game session found. Please get a task first by talking to the NPC.");
                }

                // Check if there's a current task with THIS NPC
                if (!gameState.HasActiveTask || string.IsNullOrEmpty(gameState.CurrentTaskName))
                {
                    // Check if user has an active task with a DIFFERENT NPC
                    var allUserStates = await _gameStateService.GetAllGameStatesForUserAsync(email);
                    var activeTaskWithOtherNPC = allUserStates.FirstOrDefault(s => 
                        s.HasActiveTask && 
                        !string.IsNullOrEmpty(s.CurrentTaskName) && 
                        s.RowKey != $"{game}-{npc}");

                    if (activeTaskWithOtherNPC != null)
                    {
                        var otherNpcName = activeTaskWithOtherNPC.RowKey.Split('-').LastOrDefault() ?? "another NPC";
                        
                        var casualGradingResponses = new[]
                        {
                            $"I can't grade work I didn't assign! You need to go back to {otherNpcName} for grading.",
                            $"That's not my assignment to check! {otherNpcName} gave you that task, so they should grade it.",
                            $"I'm not the right NPC for grading that work. Go back to {otherNpcName}!",
                            $"Wrong NPC! {otherNpcName} assigned that task, so they need to check your work.",
                            $"I can only grade my own assignments. {otherNpcName} is waiting to check your work!",
                            "I can't help with grading work I didn't assign. Find the right NPC!",
                            "That's not my task to grade! Go back to whoever gave you the assignment.",
                            "I only grade my own assignments. You're talking to the wrong NPC!",
                            "I can't check work I didn't assign. Find the NPC who gave you that task!",
                            "Wrong teacher! I can only grade assignments I gave out myself."
                        };

                        var randomResponse = casualGradingResponses[new Random().Next(casualGradingResponses.Length)];
                        return GameResponse.Success(randomResponse, "WRONG_NPC_FOR_GRADING");
                    }

                    return GameResponse.Error("No active task found. Please get a new task first by talking to the NPC.");
                }

                // Run the grading for the current task
                return await RunTaskGradingAsync(email, game, npc, credentials, gameState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleGameGradingAsync");
                return GameResponse.Error("Internal server error: " + ex.Message);
            }
        }

        private async Task<GameResponse> RunTaskGradingAsync(string email, string game, string npc, string credentials, GameState gameState)
        {
            try
            {
                _logger.LogInformation("Running grading for task: {taskName}", gameState.CurrentTaskName);

                // Retrieve credentials from storage if not provided
                string credentialsToUse = credentials;
                if (string.IsNullOrEmpty(credentialsToUse))
                {
                    _logger.LogInformation("No credentials provided, retrieving from storage for email: {email}", email);
                    credentialsToUse = await _storageService.GetCredentialJsonAsync(email) ?? "";
                    
                    if (string.IsNullOrEmpty(credentialsToUse))
                    {
                        var errorMessage = "No Azure credentials found. Please register your credentials first.";
                        return GameResponse.Error(errorMessage);
                    }
                }

                // Run the unit tests
                var xml = await _testRunner.RunUnitTestProcessAsync(_logger, credentialsToUse, email, gameState.CurrentTaskFilter);
                
                if (string.IsNullOrEmpty(xml))
                {
                    return GameResponse.Error("Failed to run tests. Please check your Azure setup and try again.");
                }

                // Parse test results
                var testResults = _testResultParser.ParseNUnitTestResult(xml);
                var blobName = await SaveTestResultsToStorageAsync(email, gameState.CurrentTaskName, xml, npc);

                // Check if all tests passed
                var totalTests = testResults.Count;
                var passedTests = testResults.Values.Count(v => v == 1);
                var allTestsPassed = totalTests > 0 && passedTests == totalTests;

                if (allTestsPassed)
                {
                    // Store task name and reward before completing (as they get cleared)
                    var completedTaskName = gameState.CurrentTaskName;
                    var earnedReward = gameState.CurrentTaskReward;
                    
                    // Task completed successfully - mark as completed and ready for next task
                    gameState = await _gameStateService.CompleteTaskAsync(email, game, npc, gameState.CurrentTaskName, gameState.CurrentTaskReward);
                    
                    // Use UnifiedMessageService for personalized success message
                    var personalizedSuccessMessage = await _unifiedMessageService.GetTaskCompletedMessageAsync(npc, completedTaskName, earnedReward);
                    
                    var response = GameResponse.Success(personalizedSuccessMessage, "READY_FOR_NEXT");
                    response.TaskCompleted = true;
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    response.TaskName = completedTaskName;
                    response.EasterEggUrl = await _storageService.GetRandomEasterEggAsync("Pass") ?? "";
                    
                    return response;
                }
                else
                {
                    // Some tests failed - keep the same task active
                    var failedTests = totalTests - passedTests;
                    
                    // Use UnifiedMessageService for personalized failure message
                    var personalizedMessage = await _unifiedMessageService.GetTaskFailedMessageAsync(npc, gameState.CurrentTaskName, passedTests, totalTests);
                    
                    gameState.LastMessage = personalizedMessage;
                    await _gameStateService.CreateOrUpdateGameStateAsync(gameState);
                    
                    var response = GameResponse.Success(personalizedMessage, "TASK_ASSIGNED");
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    response.TaskName = gameState.CurrentTaskName;
                    response.EasterEggUrl = await _storageService.GetRandomEasterEggAsync("Fail") ?? "";
                    response.AdditionalData["testResults"] = testResults;
                    response.AdditionalData["passedTests"] = passedTests;
                    response.AdditionalData["totalTests"] = totalTests;
                    
                    // Generate SAS URL for test result XML when tests fail
                    if (!string.IsNullOrEmpty(blobName))
                    {
                        var sasUrl = await _storageService.GenerateTestResultSasUrlAsync(blobName);
                        if (!string.IsNullOrEmpty(sasUrl))
                        {
                            response.AdditionalData["testResultXmlUrl"] = sasUrl;
                        }
                    }
                    
                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running task grading");
                var errorMessage = "Error occurred while checking your task. Please try again.";
                return GameResponse.Error(errorMessage);
            }
        }
    }
}
