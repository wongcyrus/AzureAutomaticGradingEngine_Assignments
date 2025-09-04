using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    /// <summary>
    /// Unified interface for all message personalization across the application
    /// Simplifies the rephrase logic to use consistent templates and caching
    /// </summary>
    public interface IUnifiedMessageService
    {
        /// <summary>
        /// Main method for getting personalized messages
        /// </summary>
        /// <param name="status">The message context (TASK_ASSIGNED, TASK_COMPLETED, etc.)</param>
        /// <param name="npcName">Current NPC name</param>
        /// <param name="parameters">Message parameters for template substitution</param>
        /// <returns>Personalized message</returns>
        Task<string> GetPersonalizedMessageAsync(string status, string npcName, Dictionary<string, object>? parameters = null);

        // Convenience methods for common scenarios
        Task<string> GetTaskAssignedMessageAsync(string npcName, string taskName, string instruction);
        Task<string> GetTaskCompletedMessageAsync(string npcName, string taskName, int reward);
        Task<string> GetTaskFailedMessageAsync(string npcName, string taskName, int passedTests, int totalTests);
        Task<string> GetBusyWithOtherNPCMessageAsync(string npcName, string otherNpcName);
        Task<string> GetCooldownMessageAsync(string npcName, int minutes);
        Task<string> GetAllTasksCompletedMessageAsync(string npcName);
        Task<string> GetActiveTaskReminderMessageAsync(string npcName, string taskName);
    }
}
