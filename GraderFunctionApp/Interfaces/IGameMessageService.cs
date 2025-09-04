using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IGameMessageService
    {
        Task<string> GetBusyWithOtherNPCMessageAsync(string otherNpcName, NPCCharacter? currentNpc = null);
        Task<string> GetActiveTaskReminderMessageAsync(string taskName, NPCCharacter? npc = null);
        Task<string> GetCooldownMessageAsync(int minutesRemaining, NPCCharacter? npc = null);
        Task<string> GetTaskAssignmentMessageAsync(string taskName, string instruction, NPCCharacter? npc = null);
        Task<string> GetAllTasksCompletedMessageAsync(NPCCharacter? npc = null);
        Task<string> GetGenericGreetingMessageAsync(NPCCharacter? npc = null);
    }
}
