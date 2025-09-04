using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Functions
{
    public class MessageRefreshTimerFunction
    {
        private readonly ILogger<MessageRefreshTimerFunction> _logger;
        private readonly IPreGeneratedMessageService _preGeneratedMessageService;

        public MessageRefreshTimerFunction(
            ILogger<MessageRefreshTimerFunction> logger,
            IPreGeneratedMessageService preGeneratedMessageService)
        {
            _logger = logger;
            _preGeneratedMessageService = preGeneratedMessageService;
        }

        [Function(nameof(MessageRefreshTimerFunction))]
        public async Task RunAsync([TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo) // Daily at 2 AM UTC
        {
            _logger.LogInformation("MessageRefreshTimerFunction executed at: {time}", DateTime.Now);

            try
            {
                await _preGeneratedMessageService.RefreshAllPreGeneratedMessagesAsync();
                _logger.LogInformation("Successfully refreshed all pre-generated messages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing pre-generated messages");
                throw;
            }

            if (timerInfo.ScheduleStatus is not null)
            {
                _logger.LogInformation("Next timer schedule at: {nextRun}", timerInfo.ScheduleStatus.Next);
            }
        }
    }
}
