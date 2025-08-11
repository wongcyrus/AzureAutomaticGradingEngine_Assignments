using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Functions
{
    public class PassTaskFunction
    {
        private readonly ILogger _logger;
        private readonly StorageService _storageService;

        public PassTaskFunction(ILoggerFactory loggerFactory, StorageService storageService)
        {
            _logger = loggerFactory.CreateLogger<PassTaskFunction>();
            _storageService = storageService;
        }

        [Function(nameof(GetPassTaskFunction))]
        public async Task<IActionResult> GetPassTaskFunction(
             [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Start GetPassTaskFunction");

            if (!req.Query.ContainsKey("email"))
            {
                return new BadRequestObjectResult("Email parameter is missing.");
            }

            string email = req.Query["email"]!;
            _logger.LogInformation("Fetching passed tasks for email: {email}", email);

            try
            {
                var passedTasks = await _storageService.GetPassedTasksAsync(email);
                var totalMarks = passedTasks.Sum(static task => task.Mark);

                var result = new
                {
                    TotalMarks = totalMarks,
                    PassedTasks = passedTasks.Select(static task => new { task.Name, task.Mark })
                };

                return new JsonResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch passed tasks for email: {email}", email);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
