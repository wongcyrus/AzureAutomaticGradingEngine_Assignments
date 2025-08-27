using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Functions
{
    public class PassTaskFunction
    {
        private readonly ILogger<PassTaskFunction> _logger;
        private readonly IStorageService _storageService;

        public PassTaskFunction(ILogger<PassTaskFunction> logger, IStorageService storageService)
        {
            _logger = logger;
            _storageService = storageService;
        }

        [Function(nameof(PassTaskFunction))]
        public async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Start PassTaskFunction");

            if (!req.Query.ContainsKey("email"))
            {
                return new BadRequestObjectResult(ApiResponse.ErrorResult("Email parameter is missing."));
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

                return new JsonResult(ApiResponse<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch passed tasks for email: {email}", email);
                return new ObjectResult(ApiResponse.ErrorResult("Internal server error", ex.Message))
                {
                    StatusCode = 500
                };
            }
        }
    }
}
