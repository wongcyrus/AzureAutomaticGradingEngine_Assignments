using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Services;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Constants;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Functions
{
    public class GraderFunction
    {
        private readonly ILogger _logger;
        private readonly StorageService _storageService;

        public GraderFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GraderFunction>();
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string not found");
            _storageService = new StorageService(connectionString, _logger);
        }

        [Function(nameof(AzureGraderFunction))]
        public async Task<IActionResult> AzureGraderFunction(
             [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
             ExecutionContext context)
        {
            _logger.LogInformation("Start AzureGraderFunction");

            return req.Method switch
            {
                "GET" => await HandleGetRequest(req, context),
                "POST" => await HandlePostRequest(req, context),
                _ => new OkObjectResult("ok")
            };
        }

        private async Task<IActionResult> HandleGetRequest(HttpRequest req, ExecutionContext context)
        {
            if (!req.Query.ContainsKey("credentials"))
            {
                return new ContentResult
                {
                    Content = HtmlTemplates.GraderForm,
                    ContentType = "text/html",
                    StatusCode = 200,
                };
            }

            string credentials = req.Query["credentials"]!;
            string filter = req.Query["filter"]!;
            string taskName = filter;
            string email = "Anonymous";

            _logger.LogInformation("GET Request - filter: '{filter}', taskName: '{taskName}'", filter, taskName);

            string? xml;
            if (req.Query.ContainsKey("trace"))
            {
                string trace = req.Query["trace"]!;
                email = UtilityHelpers.ExtractEmail(trace);
                _logger.LogInformation("start:" + trace);
                xml = await TestRunner.RunUnitTestProcess(context, _logger, credentials, email, filter);
                _logger.LogInformation("end:" + trace);
            }
            else
            {
                xml = await TestRunner.RunUnitTestProcess(context, _logger, credentials, email, filter);
            }

            if (string.IsNullOrEmpty(xml))
            {
                return new ContentResult { Content = "<error>Failed to run tests or no results produced.</error>", ContentType = "application/xml", StatusCode = 500 };
            }

            await SaveTestResultsToStorage(email, taskName, xml);
            return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
        }

        private async Task<IActionResult> HandlePostRequest(HttpRequest req, ExecutionContext context)
        {
            _logger.LogInformation("POST Request");
            string needXml = req.Query["xml"]!;
            string credentials = req.Form["credentials"]!;
            string filter = req.Form["filter"]!;
            string taskName = filter;

            _logger.LogInformation("POST Request - filter: '{filter}', taskName: '{taskName}'", filter, taskName);

            if (string.IsNullOrWhiteSpace(credentials))
            {
                return new ContentResult
                {
                    Content = "<result><value>No credentials</value></result>",
                    ContentType = "application/xml",
                    StatusCode = 422
                };
            }

            var xml = await TestRunner.RunUnitTestProcess(context, _logger, credentials, "Anonymous", filter);
            if (string.IsNullOrEmpty(xml))
            {
                return new ContentResult { Content = "<error>Failed to run tests or no results produced.</error>", ContentType = "application/xml", StatusCode = 500 };
            }

            await SaveTestResultsToStorage("Anonymous", taskName, xml);

            if (string.IsNullOrEmpty(needXml))
            {
                var result = TestResultParser.ParseNUnitTestResult(xml!);
                return new JsonResult(result);
            }
            return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
        }

        private async Task SaveTestResultsToStorage(string email, string taskName, string xml)
        {
            try
            {
                _logger.LogInformation("SaveTestResultsToStorage called with email: {email}", email);
                var testResults = TestResultParser.ParseNUnitTestResult(xml);
                await _storageService.SaveTestResultXmlAsync(email, xml);

                // Load all tasks and their rewards
                var tasksJson = GameTaskFunction.GetTasksJson(false);
                var allTasks = Newtonsoft.Json.JsonConvert.DeserializeObject<List<GameTaskData>>(tasksJson);
                var rewardMap = allTasks?.SelectMany(t => t.Tests.Select(test => new { test, t.Reward })).ToDictionary(x => x.test, x => x.Reward) ?? new Dictionary<string, int>();

                // Build pass dictionary with mark
                var passDict = testResults.Where(kv => kv.Value == 1)
                    .ToDictionary(kv => kv.Key, kv => rewardMap.ContainsKey(kv.Key) ? rewardMap[kv.Key] : 0);

                await _storageService.SavePassTestRecordAsync(email, taskName, passDict);
                await _storageService.SaveFailTestRecordAsync(email, taskName, testResults);
                _logger.LogInformation("Test results saved to storage for email: {email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test results to storage for email: {email}", email);
            }
        }
    }

    // PassTaskFunction moved to PassTaskFunction.cs
}
