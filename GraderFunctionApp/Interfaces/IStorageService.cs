using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IStorageService
    {
        Task<string> SaveTestResultXmlAsync(string email, string xml);
        Task SavePassTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC);
        Task SaveFailTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC);
        Task<List<(string Name, int Mark)>> GetPassedTasksAsync(string email);
        Task<List<FailTestEntity>> GetFailedTestsAsync(string email);
        Task<List<string>> GetCompletedTaskNamesAsync(string email);
        Task<int> DeletePassedTasksAsync(string email);
        Task<string?> GetLastTaskNPCAsync(string email);
        Task<string?> GetSubscriptionIdAsync(string email);
        Task<NPCCharacter?> GetNPCCharacterAsync(string npcName);
        Task<string?> GetRandomEasterEggAsync(string type);
        Task<string?> GenerateTestResultSasUrlAsync(string blobName);
    }
}
