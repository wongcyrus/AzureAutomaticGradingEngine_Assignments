using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Runtime.Caching;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Services
{
    public class AIService : IAIService
    {
        private readonly ILogger<AIService> _logger;
        private readonly AzureOpenAIOptions _options;
        private static readonly ObjectCache TokenCache = MemoryCache.Default;

        public AIService(ILogger<AIService> logger, IOptions<AzureOpenAIOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        public async Task<string?> RephraseInstructionAsync(string instruction)
        {
            if (string.IsNullOrEmpty(instruction))
            {
                return instruction;
            }

            var rnd = new Random();
            var version = rnd.Next(1, 3);
            var cacheKey = instruction + version;

            var tokenContents = TokenCache?.GetCacheItem(cacheKey);
            if (tokenContents != null && tokenContents.Value != null)
            {
                return tokenContents.Value.ToString();
            }

            if (string.IsNullOrEmpty(_options.Endpoint) || 
                string.IsNullOrEmpty(_options.ApiKey) || 
                string.IsNullOrEmpty(_options.DeploymentName))
            {
                _logger.LogWarning("Azure OpenAI configuration is incomplete. Returning original instruction.");
                return instruction;
            }

            try
            {
                var openAiClient = new AzureOpenAIClient(
                    new Uri(_options.Endpoint),
                    new AzureKeyCredential(_options.ApiKey));
                
                var client = openAiClient.GetChatClient(_options.DeploymentName);

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

                var response = await client.CompleteChatAsync(messages,
                    new ChatCompletionOptions()
                    {
                        MaxOutputTokenCount = 800,
                        Temperature = 0.9f,
                        FrequencyPenalty = 0,
                        PresencePenalty = 0
                    });

                var chatMessage = response.Value.Content[0].Text;
                var policy = new CacheItemPolicy
                {
                    Priority = CacheItemPriority.Default,
                    AbsoluteExpiration = DateTimeOffset.Now.AddHours(1)
                };
                
                tokenContents = new CacheItem(cacheKey, chatMessage);
                TokenCache?.Set(tokenContents, policy);
                
                return chatMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while rephrasing instruction with Azure OpenAI.");
                return instruction;
            }
        }
    }
}
