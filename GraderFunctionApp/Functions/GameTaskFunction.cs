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
        private readonly IAIService _aiService;
        private readonly IGameMessageService _gameMessageService;

        public GameTaskFunction(
            ILogger<GameTaskFunction> logger, 
            IGameTaskService gameTaskService,
            IGameStateService gameStateService,
            IStorageService storageService,
            IAIService aiService,
            IGameMessageService gameMessageService)
        {
            _logger = logger;
            _gameTaskService = gameTaskService;
            _gameStateService = gameStateService;
            _storageService = storageService;
            _aiService = aiService;
            _gameMessageService = gameMessageService;
        }

        [Function(nameof(GameTaskFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            var email = req.Query["email"].FirstOrDefault() ?? "unknown";
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
                var allUserStates = await _gameStateService.GetAllGameStatesForUserAsync(email);
                var activeTaskWithOtherNPC = allUserStates.FirstOrDefault(s => 
                    s.HasActiveTask && 
                    !string.IsNullOrEmpty(s.CurrentTaskName) && 
                    s.RowKey != $"{game}-{npc}");

                if (activeTaskWithOtherNPC != null)
                {
                    // Extract the other NPC name from RowKey (format: "game-npc")
                    var otherNpcName = activeTaskWithOtherNPC.RowKey.Split('-').LastOrDefault() ?? "another NPC";
                    
                    // Use GameMessageService for consistent messaging
                    var personalizedResponse = await _gameMessageService.GetBusyWithOtherNPCMessageAsync(otherNpcName, npcCharacter);
                    
                    var response = GameResponse.Success(personalizedResponse, "BUSY_WITH_OTHER_NPC");
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    response.AdditionalData["activeTaskNPC"] = otherNpcName;
                    response.AdditionalData["activeTaskName"] = activeTaskWithOtherNPC.CurrentTaskName;
                    
                    return new JsonResult(response);
                }

                // Check if user has an active task with THIS NPC
                if (gameState.HasActiveTask && !string.IsNullOrEmpty(gameState.CurrentTaskName))
                {
                    // Show the full task details including instruction
                    var activeTaskMessage = !string.IsNullOrEmpty(gameState.LastMessage) 
                        ? gameState.LastMessage 
                        : null; // Let GameMessageService handle the default message
                    
                    // Use GameMessageService for consistent messaging
                    var personalizedMessage = activeTaskMessage != null
                        ? (npcCharacter != null ? await PersonalizeMessageAsync(activeTaskMessage, npcCharacter) : activeTaskMessage)
                        : await _gameMessageService.GetActiveTaskReminderMessageAsync(gameState.CurrentTaskName, npcCharacter);
                    
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
                        var personalizedResponse = await _gameMessageService.GetCooldownMessageAsync(minutesRemaining, npcCharacter);
                        
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
                    var personalizedCompletion = await _gameMessageService.GetAllTasksCompletedMessageAsync(npcCharacter);
                        
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
                        var personalizedCompletion = await _gameMessageService.GetAllTasksCompletedMessageAsync(npcCharacter);
                            
                        var response = GameResponse.Success(personalizedCompletion, "ALL_COMPLETED");
                        response.Score = gameState.TotalScore;
                        response.CompletedTasks = gameState.CompletedTasks;
                        
                        return new JsonResult(response);
                    }
                    nextTask = uncompletedTask;
                }

                // Assign new task with personalized message
                var personalizedTaskMessage = await _gameMessageService.GetTaskAssignmentMessageAsync(nextTask.Name, nextTask.Instruction, npcCharacter);
                    
                gameState = await _gameStateService.AssignTaskAsync(email, game, npc, nextTask.Name, nextTask.Filter, nextTask.Reward, personalizedTaskMessage);

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

        private async Task<string> PersonalizeMessageAsync(string originalMessage, NPCCharacter npcCharacter)
        {
            try
            {
                var result = await _aiService.PersonalizeNPCMessageAsync(originalMessage, npcCharacter.Age, npcCharacter.Gender, npcCharacter.Background);
                return !string.IsNullOrEmpty(result) ? result : $"Tek, {originalMessage}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error personalizing message with AI, using fallback");
                return $"Tek, {originalMessage}";
            }
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
