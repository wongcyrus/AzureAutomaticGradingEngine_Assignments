using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IPreGeneratedMessageService
    {
        Task<string?> GetPreGeneratedInstructionAsync(string originalInstruction);
        Task<string?> GetPreGeneratedNPCMessageAsync(string originalMessage, int age, string gender, string background);
        Task RefreshAllPreGeneratedMessagesAsync();
        Task<PreGeneratedMessageStats> GetHitCountStatsAsync();
        Task ResetHitCountsAsync();
    }
}
