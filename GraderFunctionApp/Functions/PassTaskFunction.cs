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
        private readonly IRequestAuthenticator _requestAuthenticator;

        public PassTaskFunction(
            ILogger<PassTaskFunction> logger,
            IStorageService storageService,
            IRequestAuthenticator requestAuthenticator)
        {
            _logger = logger;
            _storageService = storageService;
            _requestAuthenticator = requestAuthenticator;
        }

        [Function(nameof(PassTaskFunction))]
        public async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Start PassTaskFunction");

            var email = _requestAuthenticator.GetAuthenticatedEmail(req);
            if (email == null)
            {
                return new UnauthorizedObjectResult(
                    ApiResponse.ErrorResult("Authentication required."));
            }

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
