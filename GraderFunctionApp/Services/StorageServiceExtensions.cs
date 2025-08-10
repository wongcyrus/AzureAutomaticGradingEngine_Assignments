using GraderFunctionApp.Models;
using System.Threading.Tasks;

namespace GraderFunctionApp.Services
{
    public static class StorageServiceExtensions
    {
        public static async Task SaveTestResultAsync(this StorageService storageService, TestResultRecord record)
        {
            await storageService.SaveTestResultXmlAsync(record.Email, "<xml>...</xml>"); // Placeholder
            await storageService.SavePassTestRecordAsync(record.Email, record.TaskName, record.Results);
            await storageService.SaveFailTestRecordAsync(record.Email, record.TaskName, record.Results);
        }
    }
}
