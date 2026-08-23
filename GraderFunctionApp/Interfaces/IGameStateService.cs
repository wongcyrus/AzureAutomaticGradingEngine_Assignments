using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IGameStateService
    {
        Task<GameState?> GetGameStateAsync(string email, string game, string npc);
        Task<GameState> CreateOrUpdateGameStateAsync(GameState gameState);
        Task<GameState> InitializeGameStateAsync(string email, string game, string npc);
        Task<GameState> UpdateGamePhaseAsync(string email, string game, string npc, string phase, string message = "");
        Task<GameState?> TryAssignTaskAsync(string email, string game, string npc, string taskName, string taskFilter, int reward, string personalizedMessage);
        Task<GameTaskLock?> GetActiveTaskLockAsync(string email);
        Task BeginGameResetAsync(string email);
        Task<int> DeleteAllGameStatesAsync(string email);
        Task EndGameResetAsync(string email);
        Task<GameState> CompleteTaskAsync(string email, string game, string npc, string taskName, int reward);
        Task<GameState?> TryUpdateActiveTaskMessageAsync(string email, string game, string npc, string taskName, string message);
        Task<List<GameState>> GetAllGameStatesForUserAsync(string email);
        Task DeleteGameStateAsync(string email, string game, string npc);
    }
}
