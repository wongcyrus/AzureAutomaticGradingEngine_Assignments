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

        public GameTaskFunction(ILogger<GameTaskFunction> logger, IGameTaskService gameTaskService)
        {
            _logger = logger;
            _gameTaskService = gameTaskService;
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
                var nextTask = await _gameTaskService.GetNextTaskAsync(email, npc, game);
                if (nextTask == null)
                {
                    return new NotFoundObjectResult(ApiResponse.ErrorResult("No available tasks found"));
                }

                var serializer = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                
                var json = JsonConvert.SerializeObject(nextTask, serializer);
                return new ContentResult 
                { 
                    Content = json, 
                    ContentType = "application/json", 
                    StatusCode = 200 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GameTaskFunction");
                return new ObjectResult(ApiResponse.ErrorResult("Internal server error", ex.Message))
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
