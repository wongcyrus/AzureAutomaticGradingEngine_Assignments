using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IGameTaskService
    {
        List<GameTaskData> GetTasks(bool rephrases);
        string GetTasksJson(bool rephrases);
        Task<GameTaskData?> GetNextTaskAsync(string email, string npc, string game);
    }
}
