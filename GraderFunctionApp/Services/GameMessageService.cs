using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Services
{
    public class GameMessageService : IGameMessageService
    {
        private readonly IAIService _aiService;
        private readonly ILogger<GameMessageService> _logger;

        // Message templates organized by scenario
        private readonly string[] _busyWithOtherNPCTemplates = new[]
        {
            "Hello there! I see you're working with {0} on a task. You should complete that first before I can help you with anything new.",
            "Hi! Looks like {0} has given you something to work on. Focus on that task first, then come back to see me!",
            "Greetings! I notice you have an active task with {0}. One task at a time - finish that one first!",
            "Hey! You're already busy with {0}'s assignment. Complete that before taking on more work!",
            "Hello! I can see {0} is keeping you busy. Finish up with them first, then we can chat!",
            "Nice to meet you! But I can see you're already working on something. One step at a time!",
            "Hi there! You look busy with your current task. Come back when you're ready for something new!",
            "Hello! I'd love to help, but you should finish your current assignment first. Good luck!",
            "Greetings! Focus on your current task for now. I'll be here when you're ready for the next challenge!",
            "Hey! Looks like you have your hands full already. Complete your current work first!"
        };

        private readonly string[] _activeTaskReminderTemplates = new[]
        {
            "Your current task: {0}. Complete it and chat with me again for grading!",
            "Don't forget about your active task: {0}. Finish it up!",
            "You still have work to do on: {0}. Let's get it done!",
            "Focus on completing: {0}. I'll be here when you're ready for grading!",
            "Remember your current assignment: {0}. Time to finish it!"
        };

        private readonly string[] _cooldownTemplates = new[]
        {
            "I just gave you a task recently! Come back in {0} minutes for a new challenge.",
            "You need to wait {0} more minutes before I can give you another task.",
            "I'm still preparing your next challenge. Return in {0} minutes!",
            "Give me {0} more minutes to prepare something new for you.",
            "You're eager! But wait {0} more minutes before I assign another task.",
            "Let me get ready for your next assignment. Check back in {0} minutes!",
            "Patience! Your next challenge will be ready in {0} minutes."
        };

        private readonly string[] _taskAssignmentTemplates = new[]
        {
            "New task: {0}. {1}",
            "Here's your next challenge: {0}. {1}",
            "Ready for this? Task: {0}. {1}",
            "Time for a new adventure: {0}. {1}",
            "Let's tackle this together: {0}. {1}"
        };

        private readonly string[] _allTasksCompletedTemplates = new[]
        {
            "Congratulations! You have completed all available tasks!",
            "Amazing work! You've mastered all the challenges I have for you.",
            "Incredible! You've completed everything. You're a true Azure expert!",
            "Fantastic job! You've conquered all my tasks. Well done!",
            "Outstanding! You've finished all challenges. You should be proud!"
        };

        private readonly string[] _genericGreetingTemplates = new[]
        {
            "Hello! How can I help you today?",
            "Greetings! What brings you to see me?",
            "Hi there! Ready for some Azure learning?",
            "Welcome! Let's see what we can accomplish together.",
            "Good to see you! What would you like to work on?"
        };

        public GameMessageService(IAIService aiService, ILogger<GameMessageService> logger)
        {
            _aiService = aiService;
            _logger = logger;
        }

        public async Task<string> GetBusyWithOtherNPCMessageAsync(string otherNpcName, NPCCharacter? currentNpc = null)
        {
            var template = GetRandomTemplate(_busyWithOtherNPCTemplates);
            var message = string.Format(template, otherNpcName);
            return await PersonalizeIfPossibleAsync(message, currentNpc);
        }

        public async Task<string> GetActiveTaskReminderMessageAsync(string taskName, NPCCharacter? npc = null)
        {
            var template = GetRandomTemplate(_activeTaskReminderTemplates);
            var message = string.Format(template, taskName);
            return await PersonalizeIfPossibleAsync(message, npc);
        }

        public async Task<string> GetCooldownMessageAsync(int minutesRemaining, NPCCharacter? npc = null)
        {
            var template = GetRandomTemplate(_cooldownTemplates);
            var message = string.Format(template, minutesRemaining);
            return await PersonalizeIfPossibleAsync(message, npc);
        }

        public async Task<string> GetTaskAssignmentMessageAsync(string taskName, string instruction, NPCCharacter? npc = null)
        {
            var template = GetRandomTemplate(_taskAssignmentTemplates);
            var message = string.Format(template, taskName, instruction);
            return await PersonalizeIfPossibleAsync(message, npc);
        }

        public async Task<string> GetAllTasksCompletedMessageAsync(NPCCharacter? npc = null)
        {
            var template = GetRandomTemplate(_allTasksCompletedTemplates);
            return await PersonalizeIfPossibleAsync(template, npc);
        }

        public async Task<string> GetGenericGreetingMessageAsync(NPCCharacter? npc = null)
        {
            var template = GetRandomTemplate(_genericGreetingTemplates);
            return await PersonalizeIfPossibleAsync(template, npc);
        }

        private async Task<string> PersonalizeIfPossibleAsync(string message, NPCCharacter? npc)
        {
            if (npc == null)
                return message;

            try
            {
                var result = await _aiService.PersonalizeNPCMessageAsync(message, npc.Age, npc.Gender, npc.Background);
                return !string.IsNullOrEmpty(result) ? result : $"Tek, {message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error personalizing message with AI, using fallback");
                return $"Tek, {message}";
            }
        }

        private static string GetRandomTemplate(string[] templates)
        {
            return templates[new Random().Next(templates.Length)];
        }
    }
}
