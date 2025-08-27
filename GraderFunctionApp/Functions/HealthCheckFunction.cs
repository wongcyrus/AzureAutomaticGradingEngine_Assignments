using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Functions
{
    public class HealthCheckFunction
    {
        private readonly ILogger<HealthCheckFunction> _logger;
        private readonly IStorageService _storageService;

        public HealthCheckFunction(ILogger<HealthCheckFunction> logger, IStorageService storageService)
        {
            _logger = logger;
            _storageService = storageService;
        }

        [Function(nameof(HealthCheckFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        {
            _logger.LogInformation("Health check requested");

            var healthStatus = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Version = "1.0.0",
                Checks = new Dictionary<string, object>()
            };

            try
            {
                // Test storage connectivity
                await _storageService.GetPassedTasksAsync("health-check-test");
                healthStatus.Checks["Storage"] = new { Status = "Healthy", ResponseTime = "< 1s" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Storage health check failed");
                healthStatus.Checks["Storage"] = new { Status = "Unhealthy", Error = ex.Message };
            }

            var isHealthy = healthStatus.Checks.Values.All(check => 
                check.GetType().GetProperty("Status")?.GetValue(check)?.ToString() == "Healthy");

            return new JsonResult(ApiResponse<object>.SuccessResult(healthStatus))
            {
                StatusCode = isHealthy ? 200 : 503
            };
        }
    }
}
