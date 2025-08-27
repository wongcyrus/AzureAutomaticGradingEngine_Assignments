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
            // For bulk operations, don't rephrase to avoid performance issues
            // Individual tasks should be rephrased when specifically requested
            return GetTasksInternal(rephrases: false);
        }

        public string GetTasksJson(bool rephrases)
        {
            // For JSON serialization of all tasks, don't rephrase
            var tasks = GetTasksInternal(rephrases: false);
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

                // Get all tasks without rephrasing first (for performance)
                var allTasks = GetTasksInternal(rephrases: false);
                
                // Filter to find available tasks
                List<GameTaskData> availableTasks;
                if (passedTasks != null && passedTasks.Any())
                {
                    var passedSet = new HashSet<string>(passedTasks);
                    availableTasks = allTasks.Where(t => !t.Tests.All(test => passedSet.Contains(test))).ToList();
                }
                else
                {
                    availableTasks = allTasks;
                }

                if (!availableTasks.Any())
                {
                    _logger.LogInformation("No available tasks found for {email}", email);
                    return null;
                }

                // Get the next task (first available)
                var nextTask = availableTasks.First();
                
                // Now rephrase ONLY the next task's instruction
                _logger.LogDebug("Rephrasing instruction for next task: {taskName}", nextTask.Name);
                var rephrasedInstruction = await _aiService.RephraseInstructionAsync(nextTask.Instruction);
                
                // Create a new task object with the rephrased instruction
                var rephrasedTask = new GameTaskData
                {
                    Name = nextTask.Name,
                    Tests = nextTask.Tests,
                    GameClassOrder = nextTask.GameClassOrder,
                    Instruction = rephrasedInstruction ?? nextTask.Instruction, // Fallback to original if rephrasing fails
                    Filter = nextTask.Filter,
                    Reward = nextTask.Reward,
                    TimeLimit = nextTask.TimeLimit
                };

                _logger.LogInformation("Returning next task for {email}: {taskName} (rephrased: {wasRephrased})", 
                    email, nextTask.Name, rephrasedInstruction != null && rephrasedInstruction != nextTask.Instruction);

                return rephrasedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get next task for email: {email}", email);
                return null;
            }
        }

        private List<GameTaskData> GetTasksInternal(bool rephrases)
        {
            var assembly = Assembly.GetAssembly(typeof(GameClassAttribute))!;
            var tasks = new List<GameTaskData>();
            
            foreach (var testClass in GetTypesWithHelpAttribute(assembly))
            {
                var gameClass = testClass.GetCustomAttribute<GameClassAttribute>();
                var methods = testClass.GetMethods()
                    .Where(m => m.GetCustomAttribute<GameTaskAttribute>() != null)
                    .Select(c => new { c.Name, GameTask = c.GetCustomAttribute<GameTaskAttribute>()! });

                // Process independent tasks (not grouped)
                var independentTasks = methods.Where(c => c.GameTask.GroupNumber == -1)
                    .Select(c => new GameTaskData()
                    {
                        Name = testClass.FullName + "." + c.Name,
                        Tests = [testClass.FullName + "." + c.Name],
                        GameClassOrder = gameClass!.Order,
                        Instruction = c.GameTask.Instruction, // Don't rephrase here
                        Filter = "test=" + testClass.FullName + "." + c.Name,
                        Reward = c.GameTask.Reward,
                        TimeLimit = c.GameTask.TimeLimit
                    });

                // Process grouped tasks
                var groupedTasks = methods.Where(c => c.GameTask.GroupNumber != -1)
                    .GroupBy(c => c.GameTask.GroupNumber)
                    .Select(c => new GameTaskData()
                    {
                        Name = string.Join(" ", c.Select(a => testClass.FullName + "." + a.Name)),
                        Tests = c.Select(a => testClass.FullName + "." + a.Name).ToArray(),
                        GameClassOrder = gameClass!.Order,
                        Instruction = string.Join("", c.Select(a => a.GameTask.Instruction)), // Don't rephrase here
                        Filter = string.Join("||", c.Select(a => "test==\"" + testClass.FullName + "." + a.Name + "\"")),
                        Reward = c.Sum(a => a.GameTask.Reward),
                        TimeLimit = c.Sum(a => a.GameTask.TimeLimit),
                    });

                tasks.AddRange(independentTasks);
                tasks.AddRange(groupedTasks);
            }

            var orderedTasks = tasks.OrderBy(c => c.GameClassOrder).ThenBy(c => c.Tests.First()).ToList();
            
            _logger.LogDebug("Generated {count} game tasks (rephrasing: {rephrases})", orderedTasks.Count, rephrases);
            return orderedTasks;
        }

        private static IEnumerable<Type> GetTypesWithHelpAttribute(Assembly assembly)
        {
            return from Type type in assembly!.GetTypes()
                   where type.GetCustomAttributes(typeof(GameClassAttribute), true).Length > 0
                   select type;
        }
    }
}
