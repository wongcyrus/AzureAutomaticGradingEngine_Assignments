using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using AzureProjectTestLib.Helper;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Services
{
    public class GameTaskService : IGameTaskService
    {
        private readonly ILogger<GameTaskService> _logger;
        private readonly IStorageService _storageService;
        private readonly IAIService _aiService;

        public GameTaskService(ILogger<GameTaskService> logger, IStorageService storageService, IAIService aiService)
        {
            _logger = logger;
            _storageService = storageService;
            _aiService = aiService;
        }

        public List<GameTaskData> GetTasks(bool rephrases)
        {
            var assembly = Assembly.GetAssembly(typeof(GameClassAttribute))!;
            var pendingTasks = new List<Task<GameTaskData>>();
            
            foreach (var testClass in GetTypesWithHelpAttribute(assembly))
            {
                var gameClass = testClass.GetCustomAttribute<GameClassAttribute>();
                var tasks = testClass.GetMethods()
                    .Where(m => m.GetCustomAttribute<GameTaskAttribute>() != null)
                    .Select(c => new { c.Name, GameTask = c.GetCustomAttribute<GameTaskAttribute>()! });

                var independentTasks = tasks.Where(c => c.GameTask.GroupNumber == -1)
                    .Select(async c => new GameTaskData()
                    {
                        Name = testClass.FullName + "." + c.Name,
                        Tests = [testClass.FullName + "." + c.Name],
                        GameClassOrder = gameClass!.Order,
                        Instruction = rephrases ? await _aiService.RephraseInstructionAsync(c.GameTask.Instruction) ?? c.GameTask.Instruction : c.GameTask.Instruction,
                        Filter = "test=" + testClass.FullName + "." + c.Name,
                        Reward = c.GameTask.Reward,
                        TimeLimit = c.GameTask.TimeLimit
                    });

                var groupedTasks = tasks.Where(c => c.GameTask.GroupNumber != -1)
                    .GroupBy(c => c.GameTask.GroupNumber)
                    .Select(async c =>
                        new GameTaskData()
                        {
                            Name = string.Join(" ", c.Select(a => testClass.FullName + "." + a.Name)),
                            Tests = c.Select(a => testClass.FullName + "." + a.Name).ToArray(),
                            GameClassOrder = gameClass!.Order,
                            Instruction = rephrases ? await _aiService.RephraseInstructionAsync(string.Join("", c.Select(a => a.GameTask.Instruction))) ?? string.Join("", c.Select(a => a.GameTask.Instruction)) : string.Join("", c.Select(a => a.GameTask.Instruction)),
                            Filter = string.Join("||", c.Select(a => "test==\"" + testClass.FullName + "." + a.Name + "\"")),
                            Reward = c.Sum(a => a.GameTask.Reward),
                            TimeLimit = c.Sum(a => a.GameTask.TimeLimit),
                        }
                    );

                pendingTasks.AddRange(independentTasks);
                pendingTasks.AddRange(groupedTasks);
            }

            var completed = Task.WhenAll(pendingTasks).GetAwaiter().GetResult();
            var ordered = completed.OrderBy(c => c.GameClassOrder).ThenBy(c => c.Tests.First()).ToList();
            
            _logger.LogInformation("Generated {count} game tasks", ordered.Count);
            return ordered;
        }

        public string GetTasksJson(bool rephrases)
        {
            var tasks = GetTasks(rephrases);
            var serializer = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
            return JsonConvert.SerializeObject(tasks, serializer);
        }

        public async Task<GameTaskData?> GetNextTaskAsync(string email, string npc, string game)
        {
            _logger.LogInformation($"GetNextTaskAsync called. Email: {email}, NPC: {npc}, Game: {game}");

            try
            {
                var passedTaskTuples = await _storageService.GetPassedTasksAsync(email);
                var passedTasks = passedTaskTuples?.Select(t => t.Name);
                _logger.LogInformation($"Retrieved {passedTasks?.Count() ?? 0} passed tasks for {email}.");

                var tasks = GetTasks(true);
                if (passedTasks != null && passedTasks.Any())
                {
                    var passedSet = new HashSet<string>(passedTasks);
                    tasks = tasks.Where(t => !t.Tests.All(test => passedSet.Contains(test))).ToList();
                }

                return tasks.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get next task for email: {email}", email);
                return null;
            }
        }

        private static IEnumerable<Type> GetTypesWithHelpAttribute(Assembly assembly)
        {
            return from Type type in assembly!.GetTypes()
                   where type.GetCustomAttributes(typeof(GameClassAttribute), true).Length > 0
                   select type;
        }
    }
}
