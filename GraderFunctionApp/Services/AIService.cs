using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Runtime.Caching;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Services
{
    public class AIService : IAIService
    {
        private readonly ILogger<AIService> _logger;
        private static readonly ObjectCache TokenCache = MemoryCache.Default;
        private readonly IPreGeneratedMessageService? _preGeneratedMessageService;

        public AIService(ILogger<AIService> logger, IPreGeneratedMessageService? preGeneratedMessageService = null)
        {
            _logger = logger;
            _preGeneratedMessageService = preGeneratedMessageService;
        }

        public async Task<string?> PersonalizeNPCMessageAsync(string message, int age, string gender, string background)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            // If message is very short or contains only special characters, return with prefix
            if (message.Trim().Length < 5 || message.Trim().All(c => !char.IsLetter(c)))
            {
                _logger.LogDebug("Message too short or no letters found, using simple prefix: {message}", message);
                return $"Tek, {message}";
            }

            // Validate NPC characteristics
            if (string.IsNullOrEmpty(gender) || string.IsNullOrEmpty(background))
            {
                _logger.LogDebug("Missing NPC characteristics (gender: {gender}, background: {background}), using simple prefix", gender, background);
                return $"Tek, {message}";
            }

            if (age < 1 || age > 200)
            {
                _logger.LogDebug("Invalid age {age}, using simple prefix", age);
                return $"Tek, {message}";
            }

            // Try to get pre-generated message first
            if (_preGeneratedMessageService != null)
            {
                try
                {
                    var preGeneratedMessage = await _preGeneratedMessageService.GetPreGeneratedNPCMessageAsync(message, age, gender, background);
                    if (!string.IsNullOrEmpty(preGeneratedMessage))
                    {
                        _logger.LogDebug("Using pre-generated NPC message for: {message}", message);
                        return preGeneratedMessage;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving pre-generated NPC message, falling back to live generation");
                }
            }

            // Check in-memory cache as secondary fallback
            var cacheKey = $"npc_{message}_{age}_{gender}_{background}";
            var tokenContents = TokenCache?.GetCacheItem(cacheKey);
            if (tokenContents != null && tokenContents.Value != null)
            {
                _logger.LogDebug("Returning cached NPC message");
                return tokenContents.Value.ToString();
            }

            var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var deploymentOrModelName = Environment.GetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME");

            if (string.IsNullOrEmpty(azureOpenAiEndpoint) || 
                string.IsNullOrEmpty(azureOpenAiApiKey) || 
                string.IsNullOrEmpty(deploymentOrModelName))
            {
                return $"Tek, {message}";
            }

            try
            {
                _logger.LogDebug("Creating Azure OpenAI client with endpoint: {endpoint}, deployment: {deployment}", azureOpenAiEndpoint, deploymentOrModelName);
                
                var endpoint = new Uri(azureOpenAiEndpoint);
                var azureClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureOpenAiApiKey));
                var chatClient = azureClient.GetChatClient(deploymentOrModelName);

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage($"You are helping in an Azure learning game. You play an NPC character with these traits - Age: {age}, Gender: {gender}, Background: {background}. Rephrase the following message to sound natural for your character while keeping all important information. Keep it friendly and educational. Return only the rephrased message."),
                    new UserChatMessage($"Rephrase this message: {message}")
                };

                var requestOptions = new ChatCompletionOptions()
                {
                    MaxOutputTokenCount = 200,
                    Temperature = 0.7f
                };

                _logger.LogDebug("Sending request to Azure OpenAI for NPC personalization. Message: {message}, Age: {age}, Gender: {gender}, Background: {background}", 
                    message, age, gender, background);
                
                var response = await chatClient.CompleteChatAsync(messages, requestOptions);

                _logger.LogDebug("Received response from Azure OpenAI. HasValue: {hasValue}, ContentCount: {contentCount}", 
                    response?.Value != null, response?.Value?.Content?.Count ?? 0);

                if (response?.Value?.Content == null || response.Value.Content.Count == 0)
                {
                    _logger.LogWarning("Azure OpenAI returned empty response for NPC personalization. Trying fallback approach. Message: {message}", message);
                    
                    // Try a simpler prompt as fallback
                    var fallbackMessages = new List<ChatMessage>
                    {
                        new SystemChatMessage("You are a helpful assistant. Rephrase the following message to be more natural and friendly while keeping the same meaning."),
                        new UserChatMessage(message)
                    };
                    
                    var fallbackResponse = await chatClient.CompleteChatAsync(fallbackMessages, requestOptions);
                    
                    if (fallbackResponse?.Value?.Content != null && fallbackResponse.Value.Content.Count > 0)
                    {
                        var fallbackResult = fallbackResponse.Value.Content[0].Text?.Trim();
                        if (!string.IsNullOrEmpty(fallbackResult))
                        {
                            _logger.LogDebug("Fallback approach succeeded. Result: {result}", fallbackResult);
                            return fallbackResult;
                        }
                    }
                    
                    _logger.LogWarning("Both original and fallback approaches failed for NPC personalization. Using default prefix.");
                    return $"Tek, {message}";
                }

                var result = response.Value.Content[0].Text?.Trim();
                
                _logger.LogDebug("Azure OpenAI response content: {content}", result);
                
                if (string.IsNullOrEmpty(result))
                {
                    _logger.LogWarning("Azure OpenAI returned empty text content for NPC personalization. Message: {message}, Age: {age}, Gender: {gender}, Background: {background}", 
                        message, age, gender, background);
                    return $"Tek, {message}";
                }
                
                // Cache the result for future use
                var policy = new CacheItemPolicy
                {
                    Priority = CacheItemPriority.Default,
                    AbsoluteExpiration = DateTimeOffset.Now.AddHours(1)
                };
                
                var cacheItem = new CacheItem(cacheKey, result);
                TokenCache?.Set(cacheItem, policy);

                _logger.LogDebug("Successfully personalized NPC message. Original: {original}, Result: {result}", message, result);
                return result;
            }
            catch (RequestFailedException ex)
            {
                // Handle specific Azure OpenAI errors
                if (ex.Status == 400 && ex.ErrorCode == "content_filter")
                {
                    _logger.LogWarning("Azure OpenAI content filter triggered for NPC message: {message}. Using fallback.", message);
                    return $"Tek, {message}";
                }
                else if (ex.Status == 429)
                {
                    _logger.LogWarning("Azure OpenAI rate limit exceeded for NPC personalization. Using fallback.");
                    return $"Tek, {message}";
                }
                else
                {
                    _logger.LogError(ex, "Azure OpenAI API request failed for NPC personalization. Status: {status}, ErrorCode: {errorCode}, Message: {errorMessage}. Endpoint: {endpoint}, Deployment: {deployment}", 
                        ex.Status, ex.ErrorCode, ex.Message, azureOpenAiEndpoint, deploymentOrModelName);
                    return $"Tek, {message}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in NPC personalization. Type: {type}, Message: {message}. Endpoint: {endpoint}, Deployment: {deployment}", 
                    ex.GetType().Name, ex.Message, azureOpenAiEndpoint, deploymentOrModelName);
                return $"Tek, {message}";
            }
        }

        public async Task<string?> RephraseInstructionAsync(string instruction)
        {
            if (string.IsNullOrEmpty(instruction))
            {
                _logger.LogDebug("Instruction is null or empty, returning as-is");
                return instruction;
            }

            // Try to get pre-generated message first
            if (_preGeneratedMessageService != null)
            {
                try
                {
                    var preGeneratedMessage = await _preGeneratedMessageService.GetPreGeneratedInstructionAsync(instruction);
                    if (!string.IsNullOrEmpty(preGeneratedMessage))
                    {
                        _logger.LogDebug("Using pre-generated instruction message for: {instruction}", instruction);
                        return preGeneratedMessage;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving pre-generated instruction message, falling back to live generation");
                }
            }

            var rnd = new Random();
            var version = rnd.Next(1, 3);
            var cacheKey = instruction + version;

            var tokenContents = TokenCache?.GetCacheItem(cacheKey);
            if (tokenContents != null && tokenContents.Value != null)
            {
                _logger.LogDebug("Returning cached rephrased instruction");
                return tokenContents.Value.ToString();
            }

            // Use original environment variable names for backward compatibility
            var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var deploymentOrModelName = Environment.GetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME");

            _logger.LogDebug("Azure OpenAI Configuration - Endpoint: {hasEndpoint}, ApiKey: {hasApiKey}, DeploymentName: {deploymentName}", 
                !string.IsNullOrEmpty(azureOpenAiEndpoint), 
                !string.IsNullOrEmpty(azureOpenAiApiKey), 
                deploymentOrModelName);

            if (string.IsNullOrEmpty(azureOpenAiEndpoint) || 
                string.IsNullOrEmpty(azureOpenAiApiKey) || 
                string.IsNullOrEmpty(deploymentOrModelName))
            {
                _logger.LogWarning("Azure OpenAI configuration is incomplete. Endpoint: {hasEndpoint}, ApiKey: {hasApiKey}, DeploymentName: {hasDeployment}. Returning original instruction.", 
                    !string.IsNullOrEmpty(azureOpenAiEndpoint), 
                    !string.IsNullOrEmpty(azureOpenAiApiKey), 
                    !string.IsNullOrEmpty(deploymentOrModelName));
                return instruction;
            }

            try
            {
                _logger.LogDebug("Initializing Azure OpenAI client with endpoint: {endpoint}", azureOpenAiEndpoint);
                
                // Follow the official SDK pattern from the documentation
                var endpoint = new Uri(azureOpenAiEndpoint);
                var azureClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureOpenAiApiKey));
                var chatClient = azureClient.GetChatClient(deploymentOrModelName);
                
                _logger.LogDebug("Created chat client for deployment: {deployment}", deploymentOrModelName);

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are a Microsoft Azure game dialogue designer, good at designing lively and interesting dialogue. " +
                                          "You only reply to instruction to ask the player setup something in Microsoft Azure."),
                    new UserChatMessage(
                        $"You need to help me rewrite a sentence with the following rule:" +
                        $"1. Keep all technical teams and Noun. " +
                        $"2. It is instructions to ask player to complete tasks." +
                        $"3. In a funny style to the brave (勇者) with some emojis" +
                        $"4. In both English and Traditional Chinese." +
                        $"5. English goes first, and Chinese goes next." +
                        $"6. Only reply to the rewritten sentence, and don't answer anything else." +
                        $"Rewrite the following sentence:\n\n\n{instruction}\n")
                };

                _logger.LogDebug("Sending request to Azure OpenAI with {messageCount} messages", messages.Count);

                // Use the official SDK pattern with ChatCompletionOptions
                var requestOptions = new ChatCompletionOptions()
                {
                    MaxOutputTokenCount = 800,
                    Temperature = 0.9f,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0
                };

                // Use the async method as recommended
                var response = await chatClient.CompleteChatAsync(messages, requestOptions);

                _logger.LogDebug("Received response from Azure OpenAI");

                if (response?.Value?.Content == null || response.Value.Content.Count == 0)
                {
                    _logger.LogWarning("Azure OpenAI returned empty response");
                    return instruction;
                }

                var chatMessage = response.Value.Content[0].Text;
                
                if (string.IsNullOrEmpty(chatMessage))
                {
                    _logger.LogWarning("Azure OpenAI returned empty text content");
                    return instruction;
                }

                var policy = new CacheItemPolicy
                {
                    Priority = CacheItemPriority.Default,
                    AbsoluteExpiration = DateTimeOffset.Now.AddHours(1)
                };
                
                tokenContents = new CacheItem(cacheKey, chatMessage);
                TokenCache?.Set(tokenContents, policy);
                
                _logger.LogInformation("Successfully rephrased instruction using Azure OpenAI. Original length: {originalLength}, Rephrased length: {rephrasedLength}", 
                    instruction.Length, chatMessage.Length);
                return chatMessage;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure OpenAI API request failed. Status: {status}, ErrorCode: {errorCode}, Message: {message}. " +
                    "Check your API key, endpoint URL, and deployment name.", 
                    ex.Status, ex.ErrorCode, ex.Message);
                return instruction;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument provided to Azure OpenAI client. " +
                    "This might indicate a configuration issue with endpoint URL or deployment name: {message}", ex.Message);
                return instruction;
            }
            catch (UriFormatException ex)
            {
                _logger.LogError(ex, "Invalid Azure OpenAI endpoint format: {endpoint}. " +
                    "Expected format: https://your-resource.openai.azure.com/ - {message}", azureOpenAiEndpoint, ex.Message);
                return instruction;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request to Azure OpenAI failed. " +
                    "This might indicate network connectivity issues or service unavailability: {message}", ex.Message);
                return instruction;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Azure OpenAI request timed out. " +
                    "The service might be overloaded or there are network issues: {message}", ex.Message);
                return instruction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while rephrasing instruction with Azure OpenAI. " +
                    "Type: {exceptionType}, Message: {message}", 
                    ex.GetType().Name, ex.Message);
                return instruction;
            }
        }
    }
}
