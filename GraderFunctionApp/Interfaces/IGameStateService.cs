using GraderFunctionApp.Models;

namespace GraderFunctionApp.Interfaces
{
    public interface IGameStateService
    {
        Task<GameState?> GetGameStateAsync(string email, string game, string npc);
        Task<GameState> CreateOrUpdateGameStateAsync(GameState gameState);
        Task<GameState> InitializeGameStateAsync(string email, string game, string npc);
        Task<GameState> UpdateGamePhaseAsync(string email, string game, string npc, string phase, string message = "");
        Task<GameState> AssignTaskAsync(string email, string game, string npc, string taskName, string taskFilter, int reward, string personalizedMessage);
        Task<GameState> CompleteTaskAsync(string email, string game, string npc, string taskName, int reward);
        Task<List<GameState>> GetAllGameStatesForUserAsync(string email);
        Task DeleteGameStateAsync(string email, string game, string npc);
    }
}
