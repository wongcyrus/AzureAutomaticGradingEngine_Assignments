using Azure.Data.Tables;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GraderFunctionApp.Services
{
    public class GameStateService : IGameStateService
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<GameStateService> _logger;
        private const string TABLE_NAME = "GameStates";

        public GameStateService(TableServiceClient tableServiceClient, ILogger<GameStateService> logger)
        {
            _tableClient = tableServiceClient.GetTableClient(TABLE_NAME);
            _tableClient.CreateIfNotExists();
            _logger = logger;
        }

        public async Task<GameState?> GetGameStateAsync(string email, string game, string npc)
        {
            try
            {
                var partitionKey = email;
                var rowKey = $"{game}-{npc}";
                
                var response = await _tableClient.GetEntityIfExistsAsync<GameState>(partitionKey, rowKey);
                return response.HasValue ? response.Value : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting game state for {email}, {game}, {npc}", email, game, npc);
                return null;
            }
        }

        public async Task<GameState> CreateOrUpdateGameStateAsync(GameState gameState)
        {
            try
            {
                gameState.LastUpdated = DateTime.UtcNow;
                await _tableClient.UpsertEntityAsync(gameState);
                return gameState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/updating game state for {partitionKey}, {rowKey}", 
                    gameState.PartitionKey, gameState.RowKey);
                throw;
            }
        }

        public async Task<GameState> InitializeGameStateAsync(string email, string game, string npc)
        {
            var gameState = new GameState
            {
                PartitionKey = email,
                RowKey = $"{game}-{npc}",
                CurrentPhase = "READY_FOR_NEXT",
                CurrentTaskName = "",
                CurrentTaskFilter = "",
                CurrentTaskReward = 0,
                LastMessage = "Welcome! Let's start your Azure learning journey!",
                ReportUrl = "",
                EasterEggUrl = "",
                LastUpdated = DateTime.UtcNow,
                TotalScore = 0,
                CompletedTasks = 0,
                CompletedTasksList = "[]",
                HasActiveTask = false
            };

            return await CreateOrUpdateGameStateAsync(gameState);
        }

        public async Task<GameState> UpdateGamePhaseAsync(string email, string game, string npc, string phase, string message = "")
        {
            var gameState = await GetGameStateAsync(email, game, npc);
            if (gameState == null)
            {
                gameState = await InitializeGameStateAsync(email, game, npc);
            }

            gameState.CurrentPhase = phase;
            if (!string.IsNullOrEmpty(message))
            {
                gameState.LastMessage = message;
            }

            return await CreateOrUpdateGameStateAsync(gameState);
        }

        public async Task<GameState> AssignTaskAsync(string email, string game, string npc, string taskName, string taskFilter, int reward, string personalizedMessage)
        {
            var gameState = await GetGameStateAsync(email, game, npc);
            if (gameState == null)
            {
                gameState = await InitializeGameStateAsync(email, game, npc);
            }

            gameState.CurrentTaskName = taskName;
            gameState.CurrentTaskFilter = taskFilter;
            gameState.CurrentTaskReward = reward;
            gameState.CurrentPhase = "TASK_ASSIGNED";
            gameState.LastMessage = personalizedMessage; // Store the already personalized message
            gameState.HasActiveTask = true;

            return await CreateOrUpdateGameStateAsync(gameState);
        }

        public async Task<GameState> CompleteTaskAsync(string email, string game, string npc, string taskName, int reward)
        {
            var gameState = await GetGameStateAsync(email, game, npc);
            if (gameState == null)
            {
                gameState = await InitializeGameStateAsync(email, game, npc);
            }

            // Update completed tasks list
            var completedTasks = JsonConvert.DeserializeObject<List<string>>(gameState.CompletedTasksList) ?? new List<string>();
            if (!completedTasks.Contains(taskName))
            {
                completedTasks.Add(taskName);
                gameState.CompletedTasksList = JsonConvert.SerializeObject(completedTasks);
                gameState.CompletedTasks = completedTasks.Count;
                gameState.TotalScore += reward;
            }

            gameState.HasActiveTask = false;
            gameState.CurrentPhase = "READY_FOR_NEXT";
            gameState.LastMessage = $"Congratulations! You completed '{taskName}' and earned {reward} points!";
            gameState.CurrentTaskName = "";
            gameState.CurrentTaskFilter = "";
            gameState.CurrentTaskReward = 0;

            return await CreateOrUpdateGameStateAsync(gameState);
        }

        public async Task<List<GameState>> GetAllGameStatesForUserAsync(string email)
        {
            try
            {
                var gameStates = new List<GameState>();
                await foreach (var entity in _tableClient.QueryAsync<GameState>(filter: $"PartitionKey eq '{email}'"))
                {
                    gameStates.Add(entity);
                }
                return gameStates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all game states for {email}", email);
                return new List<GameState>();
            }
        }

        public async Task DeleteGameStateAsync(string email, string game, string npc)
        {
            try
            {
                var partitionKey = email;
                var rowKey = $"{game}-{npc}";
                await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting game state for {email}, {game}, {npc}", email, game, npc);
                throw;
            }
        }
    }
}
