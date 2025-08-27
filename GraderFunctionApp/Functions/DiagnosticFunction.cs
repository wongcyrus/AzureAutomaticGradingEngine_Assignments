using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Functions
{
    public class DiagnosticFunction
    {
        private readonly ILogger<DiagnosticFunction> _logger;
        private readonly IAIService _aiService;

        public DiagnosticFunction(ILogger<DiagnosticFunction> logger, IAIService aiService)
        {
            _logger = logger;
            _aiService = aiService;
        }

        [Function(nameof(DiagnosticFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "diagnostic")] HttpRequest req)
        {
            _logger.LogInformation("Diagnostic function called");

            var diagnostics = new
            {
                Timestamp = DateTime.UtcNow,
                Environment = new
                {
                    AzureOpenAIEndpoint = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")) 
                        ? "Configured" : "Missing",
                    AzureOpenAIApiKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")) 
                        ? "Configured" : "Missing",
                    DeploymentName = Environment.GetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME") ?? "Missing",
                    AzureWebJobsStorage = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureWebJobsStorage")) 
                        ? "Configured" : "Missing"
                },
                Tests = new Dictionary<string, object>()
            };

            // Test Azure OpenAI using AIService
            try
            {
                _logger.LogInformation("Testing Azure OpenAI service via AIService...");
                var testInstruction = "Create a Virtual Network in Azure.";
                var result = await _aiService.RephraseInstructionAsync(testInstruction);
                
                diagnostics.Tests["AzureOpenAI_AIService"] = new
                {
                    Status = "Success",
                    OriginalInstruction = testInstruction,
                    RephrasedInstruction = result,
                    IsRephrased = !string.Equals(testInstruction, result, StringComparison.Ordinal)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI AIService test failed");
                diagnostics.Tests["AzureOpenAI_AIService"] = new
                {
                    Status = "Failed",
                    Error = ex.Message,
                    ExceptionType = ex.GetType().Name
                };
            }

            // Test Azure OpenAI directly using SDK pattern from documentation
            try
            {
                _logger.LogInformation("Testing Azure OpenAI service directly...");
                
                var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
                var deploymentOrModelName = Environment.GetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME");

                if (!string.IsNullOrEmpty(azureOpenAiEndpoint) && 
                    !string.IsNullOrEmpty(azureOpenAiApiKey) && 
                    !string.IsNullOrEmpty(deploymentOrModelName))
                {
                    // Apply the same endpoint correction as AIService
                    var correctedEndpoint = CorrectEndpointFormat(azureOpenAiEndpoint);
                    
                    // Follow the official SDK pattern
                    var endpointUri = new Uri(correctedEndpoint);
                    var azureClient = new AzureOpenAIClient(endpointUri, new AzureKeyCredential(azureOpenAiApiKey));
                    var chatClient = azureClient.GetChatClient(deploymentOrModelName);

                    var messages = new List<ChatMessage>
                    {
                        new SystemChatMessage("You are a helpful assistant."),
                        new UserChatMessage("Say 'Hello, this is a test!'")
                    };

                    var requestOptions = new ChatCompletionOptions()
                    {
                        MaxOutputTokenCount = 50,
                        Temperature = 0.1f
                    };

                    var response = await chatClient.CompleteChatAsync(messages, requestOptions);
                    var responseText = response.Value.Content[0].Text;

                    diagnostics.Tests["AzureOpenAI_Direct"] = new
                    {
                        Status = "Success",
                        Response = responseText,
                        ResponseLength = responseText?.Length ?? 0
                    };
                }
                else
                {
                    diagnostics.Tests["AzureOpenAI_Direct"] = new
                    {
                        Status = "Skipped",
                        Reason = "Configuration incomplete"
                    };
                }
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure OpenAI direct test failed with RequestFailedException");
                diagnostics.Tests["AzureOpenAI_Direct"] = new
                {
                    Status = "Failed",
                    Error = ex.Message,
                    ErrorCode = ex.ErrorCode,
                    StatusCode = ex.Status,
                    ExceptionType = ex.GetType().Name,
                    Suggestion = GetSuggestionForStatus(ex.Status)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI direct test failed");
                diagnostics.Tests["AzureOpenAI_Direct"] = new
                {
                    Status = "Failed",
                    Error = ex.Message,
                    ExceptionType = ex.GetType().Name
                };
            }

            // Test environment variables
            var envVars = new[]
            {
                "AZURE_OPENAI_ENDPOINT",
                "AZURE_OPENAI_API_KEY", 
                "DEPLOYMENT_OR_MODEL_NAME",
                "AzureWebJobsStorage"
            };

            var envStatus = envVars.ToDictionary(
                var => var,
                var => Environment.GetEnvironmentVariable(var) switch
                {
                    null => "Not Set",
                    "" => "Empty",
                    string value when var.Contains("KEY") => $"Set (length: {value.Length})",
                    string value => value.Length > 50 ? $"Set (length: {value.Length})" : value
                }
            );

            diagnostics.Tests["EnvironmentVariables"] = envStatus;

            // Configuration validation
            var configValidation = new Dictionary<string, object>();
            
            var endpointVar = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            if (!string.IsNullOrEmpty(endpointVar))
            {
                var isValidFormat = Uri.TryCreate(endpointVar, UriKind.Absolute, out var uri) && 
                    uri.Host.Contains("openai.azure.com");
                
                var isOldFormat = uri?.Host.Contains("cognitiveservices.azure.com") == true;
                
                configValidation["EndpointFormat"] = new
                {
                    Current = endpointVar,
                    IsValid = isValidFormat,
                    IsOldFormat = isOldFormat,
                    Recommendation = isOldFormat && uri != null
                        ? $"Update to: https://{uri.Host.Split('.')[0]}.openai.azure.com/"
                        : isValidFormat ? "Valid" : "Invalid format"
                };
            }

            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(apiKey))
            {
                configValidation["ApiKeyFormat"] = apiKey.Length == 32 ? "Valid Length" : $"Unexpected Length: {apiKey.Length} (expected 32)";
            }

            diagnostics.Tests["ConfigurationValidation"] = configValidation;

            return new JsonResult(ApiResponse<object>.SuccessResult(diagnostics));
        }

        private static string GetSuggestionForStatus(int status)
        {
            return status switch
            {
                401 => "Check your API key - it might be invalid or expired",
                403 => "Check if your subscription has access to Azure OpenAI and hasn't exceeded quotas",
                404 => "Check if your deployment name exists and is correctly spelled",
                429 => "Rate limit exceeded - wait a moment and try again",
                500 => "Azure OpenAI service issue - check Azure status page",
                _ => "Check your configuration and try again"
            };
        }

        private static string CorrectEndpointFormat(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
                return endpoint;

            // Remove trailing slash for processing
            var cleanEndpoint = endpoint.TrimEnd('/');

            // Check if it's using the old cognitiveservices.azure.com format
            if (cleanEndpoint.Contains("cognitiveservices.azure.com"))
            {
                // Extract the resource name and convert to new format
                var uri = new Uri(cleanEndpoint);
                var hostParts = uri.Host.Split('.');
                if (hostParts.Length > 0)
                {
                    var resourceName = hostParts[0];
                    return $"https://{resourceName}.openai.azure.com/";
                }
            }

            // Check if it's already in the correct format
            if (cleanEndpoint.Contains(".openai.azure.com"))
            {
                return cleanEndpoint + "/";
            }

            // If it doesn't match expected patterns, return as-is with trailing slash
            return cleanEndpoint + "/";
        }
    }
}
