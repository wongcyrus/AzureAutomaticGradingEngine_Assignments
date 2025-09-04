using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Services
{
    /// <summary>
    /// Unified message service that handles all NPC personalization with a simple, cacheable approach
    /// </summary>
    public class UnifiedMessageService : IUnifiedMessageService
    {
        private readonly IAIService _aiService;
        private readonly IStorageService _storageService;
        private readonly ILogger<UnifiedMessageService> _logger;

        // Simple message templates organized by status
        private static readonly Dictionary<string, string[]> MessageTemplates = new()
        {
            ["TASK_ASSIGNED"] = new[]
            {
                "New task: {TaskName}. {Instruction}",
                "Here's your next challenge: {TaskName}. {Instruction}",
                "Ready for this? Task: {TaskName}. {Instruction}"
            },
            ["TASK_COMPLETED"] = new[]
            {
                "Congratulations! You completed '{TaskName}' and earned {Reward} points!",
                "Excellent work! Task '{TaskName}' is done. You earned {Reward} points!",
                "Well done! '{TaskName}' completed successfully. {Reward} points added!"
            },
            ["TASK_FAILED"] = new[]
            {
                "Task '{TaskName}' not completed yet. {PassedTests}/{TotalTests} tests passed. Please fix the issues and try again.",
                "Almost there! {PassedTests}/{TotalTests} tests passed for '{TaskName}'. Keep working on it!",
                "Progress on '{TaskName}': {PassedTests}/{TotalTests} tests passed. You're getting closer!"
            },
            ["BUSY_WITH_OTHER_NPC"] = new[]
            {
                "Hello there! I see you're working with {OtherNPC} on a task. You should complete that first before I can help you with anything new.",
                "Hi! Looks like {OtherNPC} has given you something to work on. Focus on that task first, then come back to see me!",
                "Greetings! I notice you have an active task with {OtherNPC}. One task at a time - finish that one first!"
            },
            ["NPC_COOLDOWN"] = new[]
            {
                "I just gave you a task recently! Come back in {Minutes} minutes for a new challenge.",
                "You need to wait {Minutes} more minutes before I can give you another task.",
                "Give me {Minutes} more minutes to prepare something new for you."
            },
            ["ALL_COMPLETED"] = new[]
            {
                "Congratulations! You have completed all available tasks!",
                "Amazing work! You've mastered all the challenges I have for you.",
                "Incredible! You've completed everything. You're a true Azure expert!"
            },
            ["ACTIVE_TASK_REMINDER"] = new[]
            {
                "Your current task: {TaskName}. Complete it and chat with me again for grading!",
                "Don't forget about your active task: {TaskName}. Finish it up!",
                "You still have work to do on: {TaskName}. Let's get it done!"
            }
        };

        public UnifiedMessageService(
            IAIService aiService, 
            IStorageService storageService, 
            ILogger<UnifiedMessageService> logger)
        {
            _aiService = aiService;
            _storageService = storageService;
            _logger = logger;
        }

        /// <summary>
        /// Main method for getting personalized messages
        /// </summary>
        /// <param name="status">The message context (TASK_ASSIGNED, TASK_COMPLETED, etc.)</param>
        /// <param name="npcName">Current NPC name</param>
        /// <param name="parameters">Message parameters for template substitution</param>
        /// <returns>Personalized message</returns>
        public async Task<string> GetPersonalizedMessageAsync(
            string status, 
            string npcName, 
            Dictionary<string, object>? parameters = null)
        {
            try
            {
                // Get both main character and current NPC data
                var mainCharacter = await _storageService.GetNPCCharacterAsync("main_character");
                var npcCharacter = await _storageService.GetNPCCharacterAsync(npcName);

                // Get base message template
                var baseMessage = GetBaseMessage(status, parameters);

                // Personalize the message
                return await PersonalizeMessage(baseMessage, mainCharacter, npcCharacter, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPersonalizedMessageAsync for status: {status}, npc: {npc}", status, npcName);
                // Fallback to simple message
                var fallbackMessage = GetBaseMessage(status, parameters);
                return $"Tek, {fallbackMessage}";
            }
        }

        /// <summary>
        /// Get base message from templates with parameter substitution
        /// </summary>
        private static string GetBaseMessage(string status, Dictionary<string, object>? parameters)
        {
            if (!MessageTemplates.TryGetValue(status, out var templates))
            {
                return "Hello! How can I help you today?";
            }

            // Get random template
            var template = templates[new Random().Next(templates.Length)];

            // Replace parameters if provided
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    template = template.Replace($"{{{param.Key}}}", param.Value?.ToString() ?? "");
                }
            }

            return template;
        }

        /// <summary>
        /// Core personalization logic - simplified with consistent caching
        /// </summary>
        private async Task<string> PersonalizeMessage(
            string message, 
            NPCCharacter? mainCharacter, 
            NPCCharacter? npcCharacter, 
            string status)
        {
            // If no NPC character data, use simple fallback
            if (npcCharacter == null)
            {
                return $"Tek, {message}";
            }

            // Use the AI service for personalization with consistent parameters
            var result = await _aiService.PersonalizeNPCMessageAsync(
                message, 
                npcCharacter.Age, 
                npcCharacter.Gender, 
                npcCharacter.Background);

            return !string.IsNullOrEmpty(result) ? result : $"Tek, {message}";
        }

        /// <summary>
        /// Convenience methods for common scenarios
        /// </summary>
        public Task<string> GetTaskAssignedMessageAsync(string npcName, string taskName, string instruction)
        {
            var parameters = new Dictionary<string, object>
            {
                ["TaskName"] = taskName,
                ["Instruction"] = instruction
            };
            return GetPersonalizedMessageAsync("TASK_ASSIGNED", npcName, parameters);
        }

        public Task<string> GetTaskCompletedMessageAsync(string npcName, string taskName, int reward)
        {
            var parameters = new Dictionary<string, object>
            {
                ["TaskName"] = taskName,
                ["Reward"] = reward
            };
            return GetPersonalizedMessageAsync("TASK_COMPLETED", npcName, parameters);
        }

        public Task<string> GetTaskFailedMessageAsync(string npcName, string taskName, int passedTests, int totalTests)
        {
            var parameters = new Dictionary<string, object>
            {
                ["TaskName"] = taskName,
                ["PassedTests"] = passedTests,
                ["TotalTests"] = totalTests
            };
            return GetPersonalizedMessageAsync("TASK_FAILED", npcName, parameters);
        }

        public Task<string> GetBusyWithOtherNPCMessageAsync(string npcName, string otherNpcName)
        {
            var parameters = new Dictionary<string, object>
            {
                ["OtherNPC"] = otherNpcName
            };
            return GetPersonalizedMessageAsync("BUSY_WITH_OTHER_NPC", npcName, parameters);
        }

        public Task<string> GetCooldownMessageAsync(string npcName, int minutes)
        {
            var parameters = new Dictionary<string, object>
            {
                ["Minutes"] = minutes
            };
            return GetPersonalizedMessageAsync("NPC_COOLDOWN", npcName, parameters);
        }

        public Task<string> GetAllTasksCompletedMessageAsync(string npcName)
        {
            return GetPersonalizedMessageAsync("ALL_COMPLETED", npcName);
        }

        public Task<string> GetActiveTaskReminderMessageAsync(string npcName, string taskName)
        {
            var parameters = new Dictionary<string, object>
            {
                ["TaskName"] = taskName
            };
            return GetPersonalizedMessageAsync("ACTIVE_TASK_REMINDER", npcName, parameters);
        }
    }
}
