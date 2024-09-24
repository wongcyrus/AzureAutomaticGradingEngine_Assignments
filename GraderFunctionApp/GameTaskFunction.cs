using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using Azure;
using OpenAI.Chat;
using AzureProjectTestLib.Helper;

using System.Runtime.Caching;
using Azure.AI.OpenAI;
using Microsoft.Azure.Functions.Worker;

namespace GraderFunctionApp
{
    public class GameTaskFunction
    {
        private readonly ILogger _logger;
        private static readonly ObjectCache TokenCache = MemoryCache.Default;
        public GameTaskFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GameTaskFunction>();
        }

        private static async Task<string?> Rephrases(string sentence)
        {
            var rnd = new Random();
            var version = rnd.Next(1, 3);
            var cacheKey = sentence + version;

            var tokenContents = TokenCache?.GetCacheItem(cacheKey);
            if (tokenContents != null && tokenContents.Value != null)
            {
                return tokenContents.Value.ToString();
            }

            var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var deploymentOrModelName = Environment.GetEnvironmentVariable("DEPLOYMENT_OR_MODEL_NAME");

            if (azureOpenAiEndpoint == null || azureOpenAiApiKey == null || deploymentOrModelName == null)
                return sentence;

            AzureOpenAIClient openAiClient = new(
                new Uri(azureOpenAiEndpoint),
                new AzureKeyCredential(azureOpenAiApiKey));
            ChatClient client = openAiClient.GetChatClient(deploymentOrModelName);

            var messages = new List<ChatMessage>(){
            new SystemChatMessage("You are a Microsoft Azure game dialogue designer,Good at designing lively and interesting dialogue." +
                                  "You only reply to instruction to ask the player setup something in Microsoft Azure."),
            new UserChatMessage(
                $"You need to help me rewrite a sentence with the following rule:" +
                $"1. Keep all technical teams and Noun. " +
                $"2. It is instructions to ask player to complete tasks." +
                $"3. In a funny style to the brave (勇者) with some emojis" +
                $"4. In both English and Traditional Chinese." +
                $"5. English goes first, and Chinese goes next." +
                $"6. Only reply to the rewritten sentence, and don't answer anything else." +
                $"Rewrite the following sentence:\n\n\n{sentence}\n")
            };
            ChatCompletion response = await client.CompleteChatAsync(messages,
                new ChatCompletionOptions()
                {
                    IncludeLogProbabilities = true,
                    TopLogProbabilityCount = 3,
                    MaxTokens = 800,
                    Temperature = 0.9f,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0
                });

            var chatMessage = response.Content[0].Text;
            var policy = new CacheItemPolicy
            {
                Priority = CacheItemPriority.Default,
                // Setting expiration timing for the cache
                AbsoluteExpiration = DateTimeOffset.Now.AddHours(1)
            };
            tokenContents = new CacheItem(cacheKey, chatMessage);
            TokenCache?.Set(tokenContents, policy);
            return chatMessage;
        }

        [Function("GameTaskFunction")]
        public IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            var json = GetTasksJson(true);
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

        public static string GetTasksJson(bool rephrases)
        {
            {
                var assembly = Assembly.GetAssembly(type: typeof(GameClassAttribute))!;
                var allTasks = new List<Task<GameTaskData>>();
                foreach (var testClass in GetTypesWithHelpAttribute(assembly))
                {
                    var gameClass = testClass.GetCustomAttribute<GameClassAttribute>();
                    var tasks = testClass.GetMethods().Where(m => m.GetCustomAttribute<GameTaskAttribute>() != null)
                        .Select(c => new { c.Name, GameTask = c.GetCustomAttribute<GameTaskAttribute>()! });

                    var independentTests = tasks.Where(c => c.GameTask.GroupNumber == -1)
                        .Select(async c => new GameTaskData()
                        {
                            Name = testClass.FullName + "." + c.Name,
                            Tests = [testClass.FullName + "." + c.Name],
                            GameClassOrder = gameClass!.Order,
                            Instruction = rephrases ? await Rephrases(c.GameTask.Instruction) ?? c.GameTask.Instruction : c.GameTask.Instruction,
                            Filter = "test=" + testClass.FullName + "." + c.Name,
                            Reward = c.GameTask.Reward,
                            TimeLimit = c.GameTask.TimeLimit
                        }).ToList();


                    var groupedTasks = tasks.Where(c => c.GameTask.GroupNumber != -1)
                        .GroupBy(c => c.GameTask.GroupNumber)
                        .Select(async c =>
                            new GameTaskData()
                            {
                                Name = string.Join(" ", c.Select(a => testClass.FullName + "." + a.Name)),
                                Tests = c.Select(a => testClass.FullName + "." + a.Name).ToArray(),
                                GameClassOrder = gameClass!.Order,
                                Instruction = rephrases ? await Rephrases(string.Join("", c.Select(a => a.GameTask.Instruction))) ?? string.Join("", c.Select(a => a.GameTask.Instruction)) : string.Join("", c.Select(a => a.GameTask.Instruction)),
                                Filter =
                                    string.Join("||", c.Select(a => "test==\"" + testClass.FullName + "." + a.Name + "\"")),
                                Reward = c.Sum(a => a.GameTask.Reward),
                                TimeLimit = c.Sum(a => a.GameTask.TimeLimit),
                            }
                        ).ToList();

                    allTasks.AddRange(independentTests);
                    allTasks.AddRange(groupedTasks);
                }

                var allCompletedTask = allTasks.Select(t => t.Result).ToList();
                var serializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                allCompletedTask = allCompletedTask.OrderBy(c => c.GameClassOrder).ThenBy(c => c.Tests.First()).ToList();
                var json = JsonConvert.SerializeObject(allCompletedTask.ToArray(), serializerSettings);
                return json;
            }

            static IEnumerable<Type> GetTypesWithHelpAttribute(Assembly assembly)
            {
                return from Type type in assembly!.GetTypes()
                       where type.GetCustomAttributes(typeof(GameClassAttribute), true).Length > 0
                       select type;
            }
        }
    }
}
