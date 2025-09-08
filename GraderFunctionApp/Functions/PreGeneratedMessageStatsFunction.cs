using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Functions
{
    public class PreGeneratedMessageStatsFunction
    {
        private readonly ILogger<PreGeneratedMessageStatsFunction> _logger;
        private readonly IPreGeneratedMessageService _preGeneratedMessageService;

        public PreGeneratedMessageStatsFunction(
            ILogger<PreGeneratedMessageStatsFunction> logger,
            IPreGeneratedMessageService preGeneratedMessageService)
        {
            _logger = logger;
            _preGeneratedMessageService = preGeneratedMessageService;
        }

        [Function("PreGeneratedMessageStats")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "pregeneratedmessagestats")] HttpRequest req)
        {
            _logger.LogInformation("Getting pre-generated message statistics");

            try
            {
                var stats = await _preGeneratedMessageService.GetHitCountStatsAsync();
                
                return new OkObjectResult(new
                {
                    timestamp = DateTime.UtcNow,
                    statistics = new
                    {
                        total = new
                        {
                            messages = stats.TotalMessages,
                            hits = stats.TotalHits,
                            hitRate = stats.OverallHitRate,
                            unusedMessages = stats.UnusedMessages
                        },
                        instructions = new
                        {
                            messages = stats.InstructionMessages,
                            hits = stats.InstructionHits,
                            hitRate = stats.InstructionHitRate
                        },
                        npc = new
                        {
                            messages = stats.NPCMessages,
                            hits = stats.NPCHits,
                            hitRate = stats.NPCHitRate
                        },
                        mostUsedMessage = stats.MostUsedMessage != null ? new
                        {
                            messageType = stats.MostUsedMessage.MessageType,
                            hitCount = stats.MostUsedMessage.HitCount,
                            lastUsedAt = stats.MostUsedMessage.LastUsedAt,
                            generatedAt = stats.MostUsedMessage.GeneratedAt,
                            originalMessage = stats.MostUsedMessage.OriginalMessage?.Length > 100 
                                ? stats.MostUsedMessage.OriginalMessage.Substring(0, 100) + "..." 
                                : stats.MostUsedMessage.OriginalMessage
                        } : null,
                        leastUsedMessage = stats.LeastUsedMessage != null ? new
                        {
                            messageType = stats.LeastUsedMessage.MessageType,
                            hitCount = stats.LeastUsedMessage.HitCount,
                            lastUsedAt = stats.LeastUsedMessage.LastUsedAt,
                            generatedAt = stats.LeastUsedMessage.GeneratedAt,
                            originalMessage = stats.LeastUsedMessage.OriginalMessage?.Length > 100 
                                ? stats.LeastUsedMessage.OriginalMessage.Substring(0, 100) + "..." 
                                : stats.LeastUsedMessage.OriginalMessage
                        } : null
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pre-generated message statistics");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("ResetPreGeneratedMessageHitCounts")]
        public async Task<IActionResult> ResetHitCounts(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "pregeneratedmessagestats/reset")] HttpRequest req)
        {
            _logger.LogInformation("Resetting pre-generated message hit counts");

            try
            {
                await _preGeneratedMessageService.ResetHitCountsAsync();
                
                return new OkObjectResult(new
                {
                    message = "Hit counts reset successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting pre-generated message hit counts");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("TestCacheLookup")]
        public async Task<IActionResult> TestCacheLookup(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "pregeneratedmessagestats/test")] HttpRequest req)
        {
            var message = req.Query["message"].FirstOrDefault() ?? "Here's your next challenge: AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist AzureProjectTestLib.ResourceGroupTest.Test02_ResourceGroupLocation. Can you create a resource group named 'projProd' in Hong Kong?";
            var age = int.TryParse(req.Query["age"].FirstOrDefault(), out var ageValue) ? ageValue : 27;
            var gender = req.Query["gender"].FirstOrDefault() ?? "Female";
            var background = req.Query["background"].FirstOrDefault() ?? "Stella is an astrologer who can interpret the signs of the stars. Her knowledge provides important guidance and warnings for player during adventures.";

            _logger.LogInformation("Testing cache lookup with message: {message}, age: {age}, gender: {gender}, background: {background}", 
                message, age, gender, background);

            try
            {
                var result = await _preGeneratedMessageService.GetPreGeneratedNPCMessageAsync(message, age, gender, background);
                
                return new OkObjectResult(new
                {
                    timestamp = DateTime.UtcNow,
                    input = new { message, age, gender, background },
                    found = !string.IsNullOrEmpty(result),
                    result = result ?? "No cached message found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing cache lookup");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [Function("ClearAllPreGeneratedMessages")]
        public async Task<IActionResult> ClearAllMessages(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "pregeneratedmessagestats/clear")] HttpRequest req)
        {
            _logger.LogInformation("Clearing all pre-generated messages for testing");

            try
            {
                await _preGeneratedMessageService.ClearAllPreGeneratedMessagesAsync();
                
                return new OkObjectResult(new
                {
                    message = "All pre-generated messages cleared successfully",
                    timestamp = DateTime.UtcNow,
                    warning = "This operation is intended for testing purposes only"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all pre-generated messages");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
