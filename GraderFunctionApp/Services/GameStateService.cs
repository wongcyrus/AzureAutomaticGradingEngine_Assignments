using Azure;
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
                await EnsureResetNotInProgressAsync(gameState.PartitionKey);
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
            await EnsureResetNotInProgressAsync(email);
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

            try
            {
                await _tableClient.AddEntityAsync(gameState);
                return gameState;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return await GetGameStateAsync(email, game, npc)
                    ?? throw new InvalidOperationException(
                        "Game state was created concurrently but could not be read.");
            }
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

        public async Task<GameTaskLock?> GetActiveTaskLockAsync(string email)
        {
            var response = await _tableClient.GetEntityIfExistsAsync<GameTaskLock>(
                email,
                GameTaskLock.LockRowKey);
            return response.HasValue ? response.Value : null;
        }

        public async Task BeginGameResetAsync(string email)
        {
            var marker = new GameResetMarker
            {
                PartitionKey = email,
                StartedAt = DateTimeOffset.UtcNow
            };

            try
            {
                await _tableClient.AddEntityAsync(marker);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await _tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                    email,
                    GameResetMarker.ResetRowKey);
                var existingMarker = existing.HasValue ? existing.Value : null;
                if (existingMarker == null ||
                    existingMarker.StartedAt >= DateTimeOffset.UtcNow.AddMinutes(-5))
                {
                    throw new InvalidOperationException(
                        "A game reset is already in progress.",
                        ex);
                }

                await _tableClient.DeleteEntityAsync(
                    email,
                    GameResetMarker.ResetRowKey,
                    existingMarker.ETag);
                try
                {
                    await _tableClient.AddEntityAsync(marker);
                    return;
                }
                catch (RequestFailedException retryException)
                    when (retryException.Status == 409)
                {
                    throw new InvalidOperationException(
                        "A game reset started concurrently.",
                        retryException);
                }
            }
        }

        public async Task<int> DeleteAllGameStatesAsync(string email)
        {
            var deletedCount = 0;
            var filter = TableClient.CreateQueryFilter(
                $"PartitionKey eq {email}");

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var entities = new List<TableEntity>();
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                                   filter: filter))
                {
                    if (entity.RowKey != GameResetMarker.ResetRowKey)
                    {
                        entities.Add(entity);
                    }
                }

                if (entities.Count == 0)
                {
                    return deletedCount;
                }

                foreach (var batch in entities.Chunk(100))
                {
                    var actions = batch.Select(entity =>
                    {
                        entity.ETag = ETag.All;
                        return new TableTransactionAction(
                            TableTransactionActionType.Delete,
                            entity);
                    });
                    await _tableClient.SubmitTransactionAsync(actions);
                }

                deletedCount += entities.Count;
            }

            throw new InvalidOperationException(
                "Could not clear game state because records are still being created.");
        }

        public async Task EndGameResetAsync(string email)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(
                    email,
                    GameResetMarker.ResetRowKey,
                    ETag.All);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(
                    "Reset marker was already absent for {email}",
                    email);
            }
        }

        public async Task<GameState?> TryAssignTaskAsync(
            string email,
            string game,
            string npc,
            string taskName,
            string taskFilter,
            int reward,
            string personalizedMessage)
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
            gameState.LastUpdated = DateTime.UtcNow;

            var taskLock = new GameTaskLock
            {
                PartitionKey = email,
                Game = game,
                Npc = npc,
                TaskName = taskName
            };

            try
            {
                await EnsureResetNotInProgressAsync(email);
                await _tableClient.SubmitTransactionAsync(
                [
                    new TableTransactionAction(TableTransactionActionType.Add, taskLock),
                    new TableTransactionAction(TableTransactionActionType.UpsertReplace, gameState)
                ]);
                return gameState;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogInformation(
                    "Task assignment lock already held for {email}",
                    email);
                return null;
            }
        }

        public async Task<GameState> CompleteTaskAsync(string email, string game, string npc, string taskName, int reward)
        {
            var gameState = await GetGameStateAsync(email, game, npc);
            if (gameState == null)
            {
                throw new InvalidOperationException(
                    $"Cannot complete task '{taskName}' because its game state no longer exists.");
            }

            // Update completed tasks list
            var completedTasks = JsonConvert.DeserializeObject<List<string>>(gameState.CompletedTasksList) ?? new List<string>();
            if (!gameState.HasActiveTask ||
                !string.Equals(gameState.CurrentTaskName, taskName, StringComparison.Ordinal))
            {
                if (completedTasks.Contains(taskName))
                {
                    return gameState;
                }

                throw new InvalidOperationException(
                    $"Cannot complete task '{taskName}' because the active task changed.");
            }

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

            gameState.LastUpdated = DateTime.UtcNow;
            var taskLock = await GetActiveTaskLockAsync(email);
            await EnsureResetNotInProgressAsync(email);
            if (taskLock != null &&
                taskLock.Game == game &&
                taskLock.Npc == npc)
            {
                try
                {
                    await _tableClient.SubmitTransactionAsync(
                    [
                        new TableTransactionAction(
                            TableTransactionActionType.UpdateReplace,
                            gameState,
                            gameState.ETag),
                        new TableTransactionAction(
                            TableTransactionActionType.Delete,
                            taskLock,
                            taskLock.ETag)
                    ]);
                    return gameState;
                }
                catch (RequestFailedException ex) when (ex.Status is 404 or 412)
                {
                    return await ResolveConcurrentCompletionAsync(
                        email,
                        game,
                        npc,
                        taskName,
                        ex);
                }
            }

            if (taskLock != null)
            {
                throw new InvalidOperationException(
                    $"Cannot complete task '{taskName}' because another task owns the active lock.");
            }

            try
            {
                await _tableClient.UpdateEntityAsync(
                    gameState,
                    gameState.ETag,
                    TableUpdateMode.Replace);
                return gameState;
            }
            catch (RequestFailedException ex) when (ex.Status is 404 or 412)
            {
                return await ResolveConcurrentCompletionAsync(
                    email,
                    game,
                    npc,
                    taskName,
                    ex);
            }
        }

        public async Task<GameState?> TryUpdateActiveTaskMessageAsync(
            string email,
            string game,
            string npc,
            string taskName,
            string message)
        {
            var gameState = await GetGameStateAsync(email, game, npc);
            if (gameState == null ||
                !gameState.HasActiveTask ||
                !string.Equals(gameState.CurrentTaskName, taskName, StringComparison.Ordinal))
            {
                return null;
            }

            gameState.LastMessage = message;
            gameState.LastUpdated = DateTime.UtcNow;

            try
            {
                await EnsureResetNotInProgressAsync(email);
                await _tableClient.UpdateEntityAsync(
                    gameState,
                    gameState.ETag,
                    TableUpdateMode.Replace);
                return gameState;
            }
            catch (RequestFailedException ex) when (ex.Status is 404 or 412)
            {
                var latestState = await GetGameStateAsync(email, game, npc);
                return latestState != null &&
                    latestState.HasActiveTask &&
                    string.Equals(
                        latestState.CurrentTaskName,
                        taskName,
                        StringComparison.Ordinal)
                    ? latestState
                    : null;
            }
        }

        public async Task<List<GameState>> GetAllGameStatesForUserAsync(string email)
        {
            try
            {
                var gameStates = new List<GameState>();
                await foreach (var entity in _tableClient.QueryAsync<GameState>(filter: $"PartitionKey eq '{email}'"))
                {
                    if (entity.RowKey != GameTaskLock.LockRowKey &&
                        entity.RowKey != GameResetMarker.ResetRowKey)
                    {
                        gameStates.Add(entity);
                    }
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
                await EnsureResetNotInProgressAsync(email);
                var partitionKey = email;
                var rowKey = $"{game}-{npc}";
                var gameState = await _tableClient.GetEntityAsync<GameState>(
                    partitionKey,
                    rowKey);
                var taskLock = await GetActiveTaskLockAsync(email);
                if (taskLock != null &&
                    taskLock.Game == game &&
                    taskLock.Npc == npc)
                {
                    await _tableClient.SubmitTransactionAsync(
                    [
                        new TableTransactionAction(
                            TableTransactionActionType.Delete,
                            gameState.Value,
                            gameState.Value.ETag),
                        new TableTransactionAction(
                            TableTransactionActionType.Delete,
                            taskLock,
                            taskLock.ETag)
                    ]);
                    return;
                }

                await _tableClient.DeleteEntityAsync(
                    partitionKey,
                    rowKey,
                    gameState.Value.ETag);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting game state for {email}, {game}, {npc}", email, game, npc);
                throw;
            }
        }

        private async Task EnsureResetNotInProgressAsync(string email)
        {
            var marker = await _tableClient.GetEntityIfExistsAsync<GameResetMarker>(
                email,
                GameResetMarker.ResetRowKey);
            if (marker.HasValue)
            {
                throw new InvalidOperationException(
                    "Game progress is currently being reset.");
            }
        }

        private async Task<GameState> ResolveConcurrentCompletionAsync(
            string email,
            string game,
            string npc,
            string taskName,
            RequestFailedException concurrencyException)
        {
            var latestState = await GetGameStateAsync(email, game, npc);
            var completedTasks = latestState == null
                ? []
                : JsonConvert.DeserializeObject<List<string>>(
                    latestState.CompletedTasksList) ?? [];

            if (latestState != null && completedTasks.Contains(taskName))
            {
                return latestState;
            }

            throw new InvalidOperationException(
                $"Task '{taskName}' changed while completion was being saved.",
                concurrencyException);
        }
    }
}
