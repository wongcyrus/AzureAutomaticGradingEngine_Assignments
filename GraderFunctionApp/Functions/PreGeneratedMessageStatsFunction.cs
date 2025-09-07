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
    }
}
