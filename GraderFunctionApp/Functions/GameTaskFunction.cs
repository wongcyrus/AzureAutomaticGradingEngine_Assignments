using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Functions
{
    public class GameTaskFunction
    {
        private readonly ILogger<GameTaskFunction> _logger;
        private readonly IGameTaskService _gameTaskService;
        private readonly IGameStateService _gameStateService;
        private readonly IStorageService _storageService;

        public GameTaskFunction(
            ILogger<GameTaskFunction> logger, 
            IGameTaskService gameTaskService,
            IGameStateService gameStateService,
            IStorageService storageService)
        {
            _logger = logger;
            _gameTaskService = gameTaskService;
            _gameStateService = gameStateService;
            _storageService = storageService;
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
                    
                    var casualResponses = new[]
                    {
                        $"Hello there! I see you're working with {otherNpcName} on a task. You should complete that first before I can help you with anything new.",
                        $"Hi! Looks like {otherNpcName} has given you something to work on. Focus on that task first, then come back to see me!",
                        $"Greetings! I notice you have an active task with {otherNpcName}. One task at a time - finish that one first!",
                        $"Hey! You're already busy with {otherNpcName}'s assignment. Complete that before taking on more work!",
                        $"Hello! I can see {otherNpcName} is keeping you busy. Finish up with them first, then we can chat!",
                        "Nice to meet you! But I can see you're already working on something. One step at a time!",
                        "Hi there! You look busy with your current task. Come back when you're ready for something new!",
                        "Hello! I'd love to help, but you should finish your current assignment first. Good luck!",
                        "Greetings! Focus on your current task for now. I'll be here when you're ready for the next challenge!",
                        "Hey! Looks like you have your hands full already. Complete your current work first!"
                    };

                    var randomResponse = casualResponses[new Random().Next(casualResponses.Length)];
                    
                    var response = GameResponse.Success(randomResponse, "BUSY_WITH_OTHER_NPC");
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    response.AdditionalData["activeTaskNPC"] = otherNpcName;
                    response.AdditionalData["activeTaskName"] = activeTaskWithOtherNPC.CurrentTaskName;
                    
                    return new JsonResult(response);
                }

                // Check if user has an active task with THIS NPC
                if (gameState.HasActiveTask && !string.IsNullOrEmpty(gameState.CurrentTaskName))
                {
                    var response = GameResponse.Success(
                        $"You have an active task: '{gameState.CurrentTaskName}'. Complete it and chat with me again for grading!",
                        "TASK_ASSIGNED"
                    );
                    response.TaskName = gameState.CurrentTaskName;
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    
                    return new JsonResult(response);
                }

                // Check if this NPC was the last one to assign a task
                var lastTaskNPC = await _storageService.GetLastTaskNPCAsync(email);
                if (!string.IsNullOrEmpty(lastTaskNPC) && lastTaskNPC == npc)
                {
                    var varietyResponses = new[]
                    {
                        "You just completed my task! Why don't you try talking to other trainers for some variety?",
                        "I think you should explore what other NPCs have to offer before coming back to me!",
                        "You've been working with me recently. Go see what challenges the other trainers have!",
                        "Time to mix things up! Try getting a task from a different trainer this time.",
                        "I just gave you a task recently. Let's give other trainers a chance to teach you something new!",
                        "You should diversify your learning! Go talk to other NPCs for different perspectives.",
                        "I've been keeping you busy lately. Why not see what the other trainers are up to?",
                        "Let's spread the learning around! Try working with a different NPC for your next challenge.",
                        "You've mastered my recent assignment. Time to learn from other experts around here!",
                        "I think you'd benefit from working with different trainers. Go explore what others have to offer!"
                    };

                    var randomResponse = varietyResponses[new Random().Next(varietyResponses.Length)];
                    
                    var response = GameResponse.Success(randomResponse, "ENCOURAGE_VARIETY");
                    response.Score = gameState.TotalScore;
                    response.CompletedTasks = gameState.CompletedTasks;
                    response.AdditionalData["lastTaskNPC"] = lastTaskNPC;
                    response.AdditionalData["suggestion"] = "Try talking to a different NPC for variety";
                    
                    return new JsonResult(response);
                }

                // Get next available task
                var nextTask = await _gameTaskService.GetNextTaskAsync(email, npc, game);
                if (nextTask == null)
                {
                    var response = GameResponse.Success(
                        "Congratulations! You have completed all available tasks!",
                        "ALL_COMPLETED"
                    );
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
                        var response = GameResponse.Success(
                            "Congratulations! You have completed all available tasks!",
                            "ALL_COMPLETED"
                        );
                        response.Score = gameState.TotalScore;
                        response.CompletedTasks = gameState.CompletedTasks;
                        
                        return new JsonResult(response);
                    }
                    nextTask = uncompletedTask;
                }

                // Assign new task
                gameState = await _gameStateService.AssignTaskAsync(email, game, npc, nextTask.Name, nextTask.Filter, nextTask.Reward, nextTask.Instruction);

                var taskResponse = GameResponse.Success(
                    gameState.LastMessage,
                    "TASK_ASSIGNED"
                );
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
