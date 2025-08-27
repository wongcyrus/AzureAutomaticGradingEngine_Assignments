using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IStorageService
    {
        Task<string> SaveTestResultXmlAsync(string email, string xml);
        Task SavePassTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC);
        Task SaveFailTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC);
        Task<List<(string Name, int Mark)>> GetPassedTasksAsync(string email);
        Task<string?> GetLastTaskNPCAsync(string email);
        Task<Credential?> GetCredentialAsync(string email);
        Task<string?> GetCredentialJsonAsync(string email);
    }
}
