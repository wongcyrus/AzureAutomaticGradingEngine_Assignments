using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Functions
{
    public class MessageGeneratorFunction
    {
        private readonly ILogger<MessageGeneratorFunction> _logger;
        private readonly IPreGeneratedMessageService _preGeneratedMessageService;

        public MessageGeneratorFunction(
            ILogger<MessageGeneratorFunction> logger,
            IPreGeneratedMessageService preGeneratedMessageService)
        {
            _logger = logger;
            _preGeneratedMessageService = preGeneratedMessageService;
        }

        [Function(nameof(MessageGeneratorFunction))]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "generate-messages")] HttpRequest req)
        {
            _logger.LogInformation("MessageGeneratorFunction HTTP trigger function processed a request.");

            try
            {
                await _preGeneratedMessageService.RefreshAllPreGeneratedMessagesAsync();
                
                return new OkObjectResult(new
                {
                    success = true,
                    message = "Pre-generated messages have been successfully created and refreshed.",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating pre-generated messages");
                
                return new ObjectResult(new
                {
                    success = false,
                    message = "Error generating pre-generated messages",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                })
                {
                    StatusCode = 500
                };
            }
        }
    }
}
