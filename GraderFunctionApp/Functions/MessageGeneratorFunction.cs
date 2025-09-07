using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;
using System.Text.Json;

namespace GraderFunctionApp.Functions
{
    public class MessageGeneratorFunction
    {
        private readonly ILogger<MessageGeneratorFunction> _logger;
        private readonly IPreGeneratedMessageService _preGeneratedMessageService;
        private readonly IUnifiedMessageService _unifiedMessageService;

        public MessageGeneratorFunction(
            ILogger<MessageGeneratorFunction> logger,
            IPreGeneratedMessageService preGeneratedMessageService,
            IUnifiedMessageService unifiedMessageService)
        {
            _logger = logger;
            _preGeneratedMessageService = preGeneratedMessageService;
            _unifiedMessageService = unifiedMessageService;
        }

        /// <summary>
        /// Refreshes all pre-generated messages in the cache
        /// </summary>
        [Function("RefreshPreGeneratedMessages")]
        public async Task<IActionResult> RefreshAllMessagesAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "messages/refresh")] HttpRequest req)
        {
            _logger.LogInformation("Starting refresh of all pre-generated messages with optimized batching");

            try
            {
                await _preGeneratedMessageService.RefreshAllPreGeneratedMessagesAsync();
                
                // Get updated statistics after refresh
                var stats = await _preGeneratedMessageService.GetHitCountStatsAsync();
                
                return new OkObjectResult(new
                {
                    success = true,
                    message = "Pre-generated messages have been successfully refreshed using optimized batching",
                    statistics = new
                    {
                        totalMessages = stats.TotalMessages,
                        instructionMessages = stats.InstructionMessages,
                        npcMessages = stats.NPCMessages
                    },
                    optimizations = new
                    {
                        batchProcessing = "Enabled - 10 concurrent AI calls for NPC messages, 5 for instructions",
                        retryLogic = "3 attempts with exponential backoff",
                        rateLimiting = "2s delay between NPC batches, 1.5s for instruction batches"
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing pre-generated messages");
                
                return new ObjectResult(new
                {
                    success = false,
                    message = "Error refreshing pre-generated messages",
                    error = ex.Message,
                    recommendations = new[]
                    {
                        "Check Azure OpenAI service availability and quota",
                        "Verify network connectivity to Azure services", 
                        "Monitor function timeout (current limit: 20 minutes)",
                        "Check application logs for detailed error information"
                    },
                    timestamp = DateTime.UtcNow
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Generate a personalized message using the unified message service
        /// </summary>
        [Function("GeneratePersonalizedMessage")]
        public async Task<IActionResult> GeneratePersonalizedMessageAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "messages/personalize")] HttpRequest req)
        {
            _logger.LogInformation("Generating personalized message");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Request body is required",
                        expectedFormat = new
                        {
                            status = "TASK_ASSIGNED | TASK_COMPLETED | TASK_FAILED | etc.",
                            npcName = "string",
                            parameters = new Dictionary<string, object>
                            {
                                ["TaskName"] = "example_task",
                                ["Instruction"] = "example instruction"
                            }
                        }
                    });
                }

                var request = JsonSerializer.Deserialize<PersonalizedMessageRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (request == null || string.IsNullOrEmpty(request.Status) || string.IsNullOrEmpty(request.NpcName))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Status and NpcName are required fields"
                    });
                }

                var personalizedMessage = await _unifiedMessageService.GetPersonalizedMessageAsync(
                    request.Status,
                    request.NpcName,
                    request.Parameters
                );

                return new OkObjectResult(new
                {
                    success = true,
                    message = personalizedMessage,
                    status = request.Status,
                    npcName = request.NpcName,
                    timestamp = DateTime.UtcNow,
                    cached = IsMessageTypeCached(request.Status)
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in request body");
                return new BadRequestObjectResult(new
                {
                    success = false,
                    message = "Invalid JSON format in request body"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating personalized message");
                
                return new ObjectResult(new
                {
                    success = false,
                    message = "Error generating personalized message",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Test endpoint to generate sample messages for each status type
        /// </summary>
        [Function("TestMessageGeneration")]
        public async Task<IActionResult> TestMessageGenerationAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "messages/test")] HttpRequest req)
        {
            _logger.LogInformation("Testing message generation for all status types");

            try
            {
                var testNpc = "TestNPC";
                var results = new Dictionary<string, object>();

                // Test TASK_ASSIGNED (cached)
                var taskAssignedMsg = await _unifiedMessageService.GetTaskAssignedMessageAsync(
                    testNpc, "Deploy Virtual Machine", "Create a VM in Azure using the portal");
                results["TASK_ASSIGNED"] = new { message = taskAssignedMsg, cached = true };

                // Test TASK_COMPLETED (cached)
                var taskCompletedMsg = await _unifiedMessageService.GetTaskCompletedMessageAsync(
                    testNpc, "Deploy Virtual Machine", 100);
                results["TASK_COMPLETED"] = new { message = taskCompletedMsg, cached = true };

                // Test TASK_FAILED (not cached)
                var taskFailedMsg = await _unifiedMessageService.GetTaskFailedMessageAsync(
                    testNpc, "Deploy Virtual Machine", 2, 5);
                results["TASK_FAILED"] = new { message = taskFailedMsg, cached = false };

                // Test BUSY_WITH_OTHER_NPC (not cached)
                var busyMsg = await _unifiedMessageService.GetBusyWithOtherNPCMessageAsync(
                    testNpc, "AnotherNPC");
                results["BUSY_WITH_OTHER_NPC"] = new { message = busyMsg, cached = false };

                // Test NPC_COOLDOWN (not cached)
                var cooldownMsg = await _unifiedMessageService.GetCooldownMessageAsync(testNpc, 15);
                results["NPC_COOLDOWN"] = new { message = cooldownMsg, cached = false };

                // Test ACTIVE_TASK_REMINDER (not cached)
                var reminderMsg = await _unifiedMessageService.GetActiveTaskReminderMessageAsync(
                    testNpc, "Deploy Virtual Machine");
                results["ACTIVE_TASK_REMINDER"] = new { message = reminderMsg, cached = false };

                // Test ALL_COMPLETED (cached)
                var completedMsg = await _unifiedMessageService.GetAllTasksCompletedMessageAsync(testNpc);
                results["ALL_COMPLETED"] = new { message = completedMsg, cached = true };

                return new OkObjectResult(new
                {
                    success = true,
                    testResults = results,
                    note = "Cached messages use AI personalization and pre-generated cache. Non-cached messages skip AI for performance.",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test message generation");
                
                return new ObjectResult(new
                {
                    success = false,
                    message = "Error in test message generation",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Check if a message type is cached (uses AI personalization)
        /// </summary>
        private static bool IsMessageTypeCached(string status)
        {
            var nonCachedStatuses = new HashSet<string>
            {
                "TASK_FAILED",
                "BUSY_WITH_OTHER_NPC", 
                "NPC_COOLDOWN",
                "ACTIVE_TASK_REMINDER"
            };
            
            return !nonCachedStatuses.Contains(status);
        }
    }

    /// <summary>
    /// Request model for personalized message generation
    /// </summary>
    public class PersonalizedMessageRequest
    {
        public string Status { get; set; } = string.Empty;
        public string NpcName { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }
}
