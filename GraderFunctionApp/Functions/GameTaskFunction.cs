using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Services;

namespace GraderFunctionApp.Functions
{
    public class GameTaskFunction
    {
        private readonly ILogger<GameTaskFunction> _logger;
        private readonly IGameTaskService _gameTaskService;
        private readonly IGameStateService _gameStateService;
        private readonly IStorageService _storageService;
        private readonly IUnifiedMessageService _unifiedMessageService;
        private readonly IRequestAuthenticator _requestAuthenticator;

        public GameTaskFunction(
            ILogger<GameTaskFunction> logger, 
            IGameTaskService gameTaskService,
            IGameStateService gameStateService,
            IStorageService storageService,
            IUnifiedMessageService unifiedMessageService,
            IRequestAuthenticator requestAuthenticator)
        {
            _logger = logger;
            _gameTaskService = gameTaskService;
            _gameStateService = gameStateService;
            _storageService = storageService;
            _unifiedMessageService = unifiedMessageService;
            _requestAuthenticator = requestAuthenticator;
        }

        [Function(nameof(GameTaskFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            var email = _requestAuthenticator.GetAuthenticatedEmail(req);
            if (email == null)
            {
                return new UnauthorizedObjectResult(
                    GameResponse.Error("Authentication required."));
            }

            var npc = req.Query["npc"].FirstOrDefault() ?? "unknown";
            var game = req.Query["game"].FirstOrDefault() ?? "unknown";

            _logger.LogInformation($"GameTaskFunction called. Email: {email}, NPC: {npc}, Game: {game}");

            try
            {
                // Get or create game state for this specific NPC
                var gameState = await _gameStateService.GetGameStateAsync(email, game, npc);
                if (gameState == null)
                {
                    gameState = await _gameStateService.InitializeGameStateAsync(email, game, npc);
                }

                // Get NPC character background for personalization
                var npcCharacter = await _storageService.GetNPCCharacterAsync(npc);
                
                // Get main character background 
                var mainCharacter = await _storageService.GetNPCCharacterAsync("main_character");
                
                // Check if user has an active task with a DIFFERENT NPC
                var activeTaskLock = await _gameStateService.GetActiveTaskLockAsync(email);
                if (activeTaskLock != null &&
                    (activeTaskLock.Game != game || activeTaskLock.Npc != npc))
                {
                    return await CreateBusyResponseAsync(
                        gameState,
                        npc,
                        activeTaskLock.Npc,
                        activeTaskLock.TaskName);
                }

                var allUserStates = await _gameStateService.GetAllGameStatesForUserAsync(email);
                var activeTaskWithOtherNPC = allUserStates.FirstOrDefault(s => 
                    s.HasActiveTask && 
                    !string.IsNullOrEmpty(s.CurrentTaskName) && 
                    s.RowKey != $"{game}-{npc}");

                if (activeTaskWithOtherNPC != null)
                {
                    // Extract the other NPC name from RowKey (format: "game-npc")
                    var otherNpcName = activeTaskWithOtherNPC.RowKey.Split('-').LastOrDefault() ?? "another NPC";
                    return await CreateBusyResponseAsync(
                        gameState,
                        npc,
                        otherNpcName,
                        activeTaskWithOtherNPC.CurrentTaskName);
                }

                // Check if user has an active task with THIS NPC
                if (gameState.HasActiveTask && !string.IsNullOrEmpty(gameState.CurrentTaskName))
                {
                    // Use the stored personalized message (already personalized when task was assigned)
                    var personalizedMessage = !string.IsNullOrEmpty(gameState.LastMessage) 
                        ? gameState.LastMessage  // Already personalized, don't personalize again
                        : await _unifiedMessageService.GetActiveTaskReminderMessageAsync(npc, gameState.CurrentTaskName);
                    
                    var response = GameResponse.Success(personalizedMessage, "TASK_ASSIGNED");
                    response.TaskName = gameState.CurrentTaskName;
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    
                    return new JsonResult(response);
                }

                // Check if this NPC assigned a task recently (within 1 hour)
                var lastTaskNPC = await _storageService.GetLastTaskNPCAsync(email);
                if (!string.IsNullOrEmpty(lastTaskNPC) && lastTaskNPC == npc)
                {
                    // Check if the last task from this NPC was assigned within the last hour
                    var lastTaskTime = gameState.LastUpdated;
                    var oneHourAgo = DateTime.UtcNow.AddHours(-1);
                    
                    if (lastTaskTime > oneHourAgo)
                    {
                        var timeRemaining = lastTaskTime.AddHours(1) - DateTime.UtcNow;
                        var minutesRemaining = (int)Math.Ceiling(timeRemaining.TotalMinutes);
                        
                        // Use GameMessageService for consistent messaging
                        var personalizedResponse = await _unifiedMessageService.GetCooldownMessageAsync(npc, minutesRemaining);
                        
                        var response = GameResponse.Success(personalizedResponse, "NPC_COOLDOWN");
                        response.Score = gameState.TotalScore;
                        response.CompletedTasks = gameState.CompletedTasks;
                        response.AdditionalData["cooldownMinutes"] = minutesRemaining;
                        response.AdditionalData["nextAvailableTime"] = lastTaskTime.AddHours(1);
                        
                        return new JsonResult(response);
                    }
                }

                // Get next available task
                var nextTask = await _gameTaskService.GetNextTaskAsync(email, npc, game);
                if (nextTask == null)
                {
                    var personalizedCompletion = await _unifiedMessageService.GetAllTasksCompletedMessageAsync(npc);
                        
                    var response = GameResponse.Success(personalizedCompletion, "ALL_COMPLETED");
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    
                    return new JsonResult(response);
                }

                // Check if task is already completed
                var completedTasks = JsonConvert.DeserializeObject<List<string>>(gameState.CompletedTasksList) ?? new List<string>();
                if (completedTasks.Contains(nextTask.Name))
                {
                    // Find next uncompleted task
                    var allTasks = _gameTaskService.GetTasks(false);
                    var uncompletedTask = allTasks.FirstOrDefault(t => !completedTasks.Contains(t.Name));
                    
                    if (uncompletedTask == null)
                    {
                        var personalizedCompletion = await _unifiedMessageService.GetAllTasksCompletedMessageAsync(npc);
                            
                        var response = GameResponse.Success(personalizedCompletion, "ALL_COMPLETED");
                        response.Score = gameState.TotalScore;
                        response.CompletedTasks = gameState.CompletedTasks;
                        
                        return new JsonResult(response);
                    }
                    nextTask = uncompletedTask;
                }

                // Assign new task with personalized message
                var personalizedTaskMessage = await _unifiedMessageService.GetTaskAssignedMessageAsync(npc, nextTask.Name, nextTask.Instruction);
                    
                gameState = await _gameStateService.TryAssignTaskAsync(
                    email,
                    game,
                    npc,
                    nextTask.Name,
                    nextTask.Filter,
                    nextTask.Reward,
                    personalizedTaskMessage);

                if (gameState == null)
                {
                    activeTaskLock = await _gameStateService.GetActiveTaskLockAsync(email);
                    if (activeTaskLock == null)
                    {
                        throw new InvalidOperationException(
                            "Task assignment lock was lost during concurrent assignment.");
                    }

                    var currentState = await _gameStateService.GetGameStateAsync(
                        email,
                        activeTaskLock.Game,
                        activeTaskLock.Npc) ?? new GameState();
                    if (activeTaskLock.Game == game && activeTaskLock.Npc == npc)
                    {
                        var activeResponse = GameResponse.Success(
                            currentState.LastMessage,
                            "TASK_ASSIGNED");
                        activeResponse.TaskName = activeTaskLock.TaskName;
                        activeResponse.Score = currentState.TotalScore;
                        activeResponse.CompletedTasks = currentState.CompletedTasks;
                        return new JsonResult(activeResponse);
                    }

                    return await CreateBusyResponseAsync(
                        currentState,
                        npc,
                        activeTaskLock.Npc,
                        activeTaskLock.TaskName);
                }

                var taskResponse = GameResponse.Success(personalizedTaskMessage, "TASK_ASSIGNED");
                taskResponse.TaskName = nextTask.Name;
                taskResponse.Score = gameState.TotalScore;
                taskResponse.CompletedTasks = gameState.CompletedTasks;
                taskResponse.AdditionalData["instruction"] = nextTask.Instruction;
                taskResponse.AdditionalData["timeLimit"] = nextTask.TimeLimit;
                taskResponse.AdditionalData["reward"] = nextTask.Reward;
                taskResponse.AdditionalData["tests"] = nextTask.Tests;

                _logger.LogInformation("Assigned new task '{taskName}' to {email}", nextTask.Name, email);
                
                return new JsonResult(taskResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GameTaskFunction");
                return new ObjectResult(GameResponse.Error("Internal server error: " + ex.Message))
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<IActionResult> CreateBusyResponseAsync(
            GameState gameState,
            string requestedNpc,
            string activeNpc,
            string activeTaskName)
        {
            var personalizedResponse =
                await _unifiedMessageService.GetBusyWithOtherNPCMessageAsync(
                    requestedNpc,
                    activeNpc);
            var response = GameResponse.Success(
                personalizedResponse,
                "BUSY_WITH_OTHER_NPC");
            response.Score = gameState.TotalScore;
            response.CompletedTasks = gameState.CompletedTasks;
            response.AdditionalData["activeTaskNPC"] = activeNpc;
            response.AdditionalData["activeTaskName"] = activeTaskName;
            return new JsonResult(response);
        }

        // Keep these methods for backward compatibility with existing code
        public List<GameTaskData> GetTasks(bool rephrases)
        {
            return _gameTaskService.GetTasks(rephrases);
        }

        public string GetTasksJson(bool rephrases)
        {
            return _gameTaskService.GetTasksJson(rephrases);
        }
    }
}
