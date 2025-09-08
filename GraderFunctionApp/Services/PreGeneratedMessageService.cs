using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraderFunctionApp.Configuration;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace GraderFunctionApp.Services
{
    public class PreGeneratedMessageService : IPreGeneratedMessageService
    {
        private readonly ILogger<PreGeneratedMessageService> _logger;
        private readonly TableClient _tableClient;
        private readonly IAIService _aiService;
        private readonly IGameTaskService _gameTaskService;
        private readonly TableServiceClient _tableServiceClient;
        private readonly StorageOptions _storageOptions;

        public PreGeneratedMessageService(
            ILogger<PreGeneratedMessageService> logger,
            TableServiceClient tableServiceClient,
            IOptions<StorageOptions> storageOptions,
            IAIService aiService,
            IGameTaskService gameTaskService)
        {
            _logger = logger;
            _tableServiceClient = tableServiceClient;
            _storageOptions = storageOptions.Value;
            _tableClient = tableServiceClient.GetTableClient(_storageOptions.PreGeneratedMessageTableName);
            _aiService = aiService;
            _gameTaskService = gameTaskService;
        }

        public async Task<string?> GetPreGeneratedInstructionAsync(string originalInstruction)
        {
            if (string.IsNullOrEmpty(originalInstruction))
                return null;

            try
            {
                var messageHash = ComputeHash(originalInstruction);
                var response = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("instruction", messageHash);

                if (response.HasValue && response.Value != null)
                {
                    var preGeneratedMessage = response.Value;
                    _logger.LogDebug("Found pre-generated instruction message for hash: {hash}", messageHash);
                    
                    // Increment hit count and update last used timestamp
                    await IncrementHitCountAsync(preGeneratedMessage);
                    
                    return preGeneratedMessage?.GeneratedMessage;
                }

                _logger.LogDebug("No pre-generated instruction message found for hash: {hash}", messageHash);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pre-generated instruction message");
                return null;
            }
        }

        public async Task<string?> GetPreGeneratedNPCMessageAsync(string originalMessage, int age, string gender, string background)
        {
            if (string.IsNullOrEmpty(originalMessage))
                return null;

            try
            {
                // Use EXACT same key format as AIService
                var messageHash = ComputeHash(originalMessage);
                var npcKey = $"{age}_{gender.GetHashCode()}_{background.GetHashCode()}";
                var cacheKey = $"npc_{npcKey}_{messageHash}";
                
                _logger.LogInformation("GetPreGeneratedNPCMessageAsync - Looking for message. OriginalMessage: {originalMessage}, MessageHash: {messageHash}, NPCKey: {npcKey}, CacheKey: {cacheKey}", 
                    originalMessage, messageHash, npcKey, cacheKey);

                // Use cacheKey directly as RowKey (not hashed again)
                var response = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("npc", cacheKey);

                if (response.HasValue && response.Value != null)
                {
                    var preGeneratedMessage = response.Value;
                    _logger.LogInformation("FOUND pre-generated NPC message! CacheKey: {cacheKey}, Current HitCount: {hitCount}, MessageType: {messageType}", 
                        cacheKey, preGeneratedMessage.HitCount, preGeneratedMessage.MessageType);
                    
                    // Increment hit count and update last used timestamp
                    _logger.LogInformation("About to increment hit count for message cacheKey: {cacheKey}", cacheKey);
                    await IncrementHitCountAsync(preGeneratedMessage);
                    
                    return preGeneratedMessage?.GeneratedMessage;
                }

                _logger.LogInformation("NO pre-generated NPC message found for cacheKey: {cacheKey}", cacheKey);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pre-generated NPC message");
                return null;
            }
        }

        public async Task RefreshAllPreGeneratedMessagesAsync()
        {
            _logger.LogInformation("Starting refresh of all pre-generated messages from database");
            
            try
            {
                // Create table if it doesn't exist
                await _tableClient.CreateIfNotExistsAsync();

                // Generate messages with optimized batching to avoid timeouts
                _logger.LogInformation("Generating instruction messages from tasks...");
                await GenerateInstructionMessagesFromTasksAsync();
                
                _logger.LogInformation("Generating NPC messages from database...");
                await GenerateNPCMessagesFromDatabaseOptimizedAsync();

                _logger.LogInformation("Completed refresh of all pre-generated messages from database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pre-generated messages refresh");
                throw;
            }
        }

        private async Task GenerateInstructionMessagesFromTasksAsync()
        {
            try
            {
                _logger.LogInformation("Loading task instructions from GameTaskService");
                
                // Get all tasks from the system
                var allTasks = _gameTaskService.GetTasks(rephrases: false);
                _logger.LogInformation("Found {count} tasks to process for instruction pre-generation", allTasks.Count);

                var tasksWithInstructions = allTasks.Where(t => !string.IsNullOrEmpty(t.Instruction)).ToList();
                _logger.LogInformation("Processing {count} tasks with valid instructions", tasksWithInstructions.Count);

                // Process in smaller batches with parallel execution
                const int batchSize = 5; // Process 5 AI requests in parallel for instructions
                const int delayBetweenBatches = 1500; // 1.5 second delay between batches

                var totalBatches = (int)Math.Ceiling((double)tasksWithInstructions.Count / batchSize);
                var processedCount = 0;

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var batch = tasksWithInstructions.Skip(batchIndex * batchSize).Take(batchSize).ToList();
                    _logger.LogInformation("Processing instruction batch {batchIndex}/{totalBatches} ({count} items)...", 
                        batchIndex + 1, totalBatches, batch.Count);

                    // Process batch in parallel
                    var batchTasks = batch.Select(async task =>
                    {
                        try
                        {
                            await GenerateAndStoreInstructionAsync(task.Instruction);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate instruction for task: {taskName}", task.Name);
                            return false;
                        }
                    });

                    var results = await Task.WhenAll(batchTasks);
                    var successCount = results.Count(r => r);
                    processedCount += successCount;

                    _logger.LogInformation("Instruction batch {batchIndex}/{totalBatches} completed: {successCount}/{totalItems} successful. Total processed: {processedCount}/{totalTasks}",
                        batchIndex + 1, totalBatches, successCount, batch.Count, processedCount, tasksWithInstructions.Count);

                    // Add delay between batches
                    if (batchIndex < totalBatches - 1)
                    {
                        await Task.Delay(delayBetweenBatches);
                    }
                }

                _logger.LogInformation("Processed {count} task instructions for pre-generation using optimized batch processing", processedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating instruction messages from tasks");
                throw;
            }
        }

        private async Task GenerateAndStoreNPCMessageAsync(string originalMessage, int age, string gender, string background)
        {
            const int maxRetries = 3;
            var baseDelay = TimeSpan.FromMilliseconds(500);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Use EXACT same key format as AIService
                    var messageHash = ComputeHash(originalMessage);
                    var npcKey = $"{age}_{gender.GetHashCode()}_{background.GetHashCode()}";
                    var cacheKey = $"npc_{npcKey}_{messageHash}";

                    _logger.LogInformation("GenerateAndStoreNPCMessageAsync - Attempt {attempt}. OriginalMessage: {originalMessage}, MessageHash: {messageHash}, NPCKey: {npcKey}, CacheKey: {cacheKey}", 
                        attempt, originalMessage, messageHash, npcKey, cacheKey);

                    // Check if already exists using cacheKey directly
                    var existingResponse = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("npc", cacheKey);
                    if (existingResponse.HasValue && existingResponse.Value != null)
                    {
                        _logger.LogInformation("GenerateAndStoreNPCMessageAsync - NPC message already exists for cacheKey: {cacheKey}, skipping generation", cacheKey);
                        return;
                    }

                    _logger.LogInformation("GenerateAndStoreNPCMessageAsync - Calling AI service to generate message for: {originalMessage}", originalMessage);
                    
                    // Generate new message using AI service
                    var generatedMessage = await _aiService.PersonalizeNPCMessageAsync(originalMessage, age, gender, background);

                    if (!string.IsNullOrEmpty(generatedMessage) && generatedMessage != $"Tek, {originalMessage}")
                    {
                        _logger.LogInformation("GenerateAndStoreNPCMessageAsync - AI service returned message: {generatedMessage}", generatedMessage);
                        
                        // Store NPC characteristics for reference
                        var npcCharacteristics = JsonConvert.SerializeObject(new { age, gender, background });
                        
                        var preGeneratedMessage = new PreGeneratedMessage
                        {
                            PartitionKey = "npc",
                            RowKey = cacheKey, // Use cacheKey directly, not hashed
                            OriginalMessage = originalMessage,
                            GeneratedMessage = generatedMessage,
                            MessageType = "npc",
                            NPCCharacteristics = npcCharacteristics,
                            GeneratedAt = DateTime.UtcNow
                        };

                        _logger.LogInformation("GenerateAndStoreNPCMessageAsync - About to store message in table. CacheKey: {cacheKey}, GeneratedMessage: {generatedMessage}", 
                            cacheKey, generatedMessage);

                        await _tableClient.UpsertEntityAsync(preGeneratedMessage);
                        
                        _logger.LogInformation("GenerateAndStoreNPCMessageAsync SUCCESS - Stored message with cacheKey: {cacheKey}", cacheKey);
                    }
                    else
                    {
                        _logger.LogWarning("GenerateAndStoreNPCMessageAsync - AI service returned empty or fallback message: {generatedMessage}", generatedMessage);
                    }
                    
                    return; // Success - exit retry loop
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    _logger.LogWarning(ex, "Attempt {attempt}/{maxRetries} failed for NPC message: {message}. Retrying in {delay}ms", 
                        attempt, maxRetries, originalMessage, delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "All {maxRetries} attempts failed for NPC message: {message}", maxRetries, originalMessage);
                    // Don't rethrow - let the batch continue processing
                    return;
                }
            }
        }

        private async Task GenerateAndStoreInstructionAsync(string originalInstruction)
        {
            const int maxRetries = 3;
            var baseDelay = TimeSpan.FromMilliseconds(300);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var messageHash = ComputeHash(originalInstruction);
                    
                    // Check if already exists
                    var existingResponse = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("instruction", messageHash);
                    if (existingResponse.HasValue && existingResponse.Value != null)
                    {
                        _logger.LogDebug("Instruction message already exists for hash: {hash}", messageHash);
                        return;
                    }

                    // Generate new message using AI service
                    var generatedMessage = await _aiService.RephraseInstructionAsync(originalInstruction);
                    
                    if (!string.IsNullOrEmpty(generatedMessage) && generatedMessage != originalInstruction)
                    {
                        var preGeneratedMessage = new PreGeneratedMessage
                        {
                            PartitionKey = "instruction",
                            RowKey = messageHash,
                            OriginalMessage = originalInstruction,
                            GeneratedMessage = generatedMessage,
                            MessageType = "instruction",
                            GeneratedAt = DateTime.UtcNow
                        };

                        await _tableClient.UpsertEntityAsync(preGeneratedMessage);
                        _logger.LogDebug("Stored pre-generated instruction message for hash: {hash}", messageHash);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping instruction - AI returned empty or unchanged response for: {instruction}", originalInstruction);
                    }
                    
                    return; // Success - exit retry loop
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    _logger.LogWarning(ex, "Attempt {attempt}/{maxRetries} failed for instruction: {instruction}. Retrying in {delay}ms", 
                        attempt, maxRetries, originalInstruction, delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "All {maxRetries} attempts failed for instruction: {instruction}", maxRetries, originalInstruction);
                    // Don't rethrow - let the batch continue processing
                    return;
                }
            }
        }

        public async Task<PreGeneratedMessageStats> GetHitCountStatsAsync()
        {
            _logger.LogInformation("Retrieving hit count statistics for pre-generated messages");
            
            var stats = new PreGeneratedMessageStats();
            var allMessages = new List<PreGeneratedMessage>();
            
            try
            {
                // Query all messages from the table
                await foreach (var message in _tableClient.QueryAsync<PreGeneratedMessage>())
                {
                    allMessages.Add(message);
                }
                
                // Calculate overall stats
                stats.TotalMessages = allMessages.Count;
                stats.TotalHits = allMessages.Sum(m => m.HitCount);
                stats.UnusedMessages = allMessages.Count(m => m.HitCount == 0);
                
                // Calculate instruction-specific stats
                var instructionMessages = allMessages.Where(m => m.MessageType == "instruction").ToList();
                stats.InstructionMessages = instructionMessages.Count;
                stats.InstructionHits = instructionMessages.Sum(m => m.HitCount);
                
                // Calculate NPC-specific stats
                var npcMessages = allMessages.Where(m => m.MessageType == "npc").ToList();
                stats.NPCMessages = npcMessages.Count;
                stats.NPCHits = npcMessages.Sum(m => m.HitCount);
                
                // Find most and least used messages
                if (allMessages.Any())
                {
                    stats.MostUsedMessage = allMessages.OrderByDescending(m => m.HitCount).FirstOrDefault();
                    stats.LeastUsedMessage = allMessages.OrderBy(m => m.HitCount).FirstOrDefault();
                }
                
                _logger.LogInformation("Hit count statistics: Total messages: {total}, Total hits: {hits}, Hit rate: {rate:P2}", 
                    stats.TotalMessages, stats.TotalHits, stats.OverallHitRate);
                    
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving hit count statistics");
                return stats; // Return empty stats rather than throw
            }
        }

        public async Task ResetHitCountsAsync()
        {
            _logger.LogInformation("Resetting all hit counts for pre-generated messages");
            
            try
            {
                var messagesToUpdate = new List<PreGeneratedMessage>();
                
                // Query all messages with hit counts > 0
                await foreach (var message in _tableClient.QueryAsync<PreGeneratedMessage>(filter: "HitCount gt 0"))
                {
                    messagesToUpdate.Add(message);
                }
                
                _logger.LogInformation("Found {count} messages with hit counts to reset", messagesToUpdate.Count);
                
                // Reset hit counts in batches
                foreach (var message in messagesToUpdate)
                {
                    try
                    {
                        message.HitCount = 0;
                        message.LastUsedAt = null;
                        await _tableClient.UpdateEntityAsync(message, message.ETag, TableUpdateMode.Replace);
                    }
                    catch (RequestFailedException ex) when (ex.Status == 412)
                    {
                        // Concurrency conflict - log and continue
                        _logger.LogDebug("Concurrency conflict when resetting hit count for message hash: {hash}", message.RowKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reset hit count for message hash: {hash}", message.RowKey);
                    }
                }
                
                _logger.LogInformation("Completed resetting hit counts for pre-generated messages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting hit counts for pre-generated messages");
                throw;
            }
        }

        public async Task ClearAllPreGeneratedMessagesAsync()
        {
            _logger.LogInformation("Starting cleanup of all pre-generated messages for testing");
            
            try
            {
                var messagesToDelete = new List<PreGeneratedMessage>();
                
                // Query all messages
                await foreach (var message in _tableClient.QueryAsync<PreGeneratedMessage>())
                {
                    messagesToDelete.Add(message);
                }
                
                _logger.LogInformation("Found {count} pre-generated messages to delete", messagesToDelete.Count);
                
                if (!messagesToDelete.Any())
                {
                    _logger.LogInformation("No pre-generated messages found to delete");
                    return;
                }
                
                // Delete messages in batches to avoid timeout
                const int batchSize = 100;
                var totalBatches = (int)Math.Ceiling((double)messagesToDelete.Count / batchSize);
                var deletedCount = 0;
                
                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var batch = messagesToDelete.Skip(batchIndex * batchSize).Take(batchSize).ToList();
                    
                    foreach (var message in batch)
                    {
                        try
                        {
                            await _tableClient.DeleteEntityAsync(message.PartitionKey, message.RowKey, message.ETag);
                            deletedCount++;
                        }
                        catch (RequestFailedException ex) when (ex.Status == 404)
                        {
                            // Entity already deleted - this is fine
                            _logger.LogDebug("Message already deleted: {partitionKey}/{rowKey}", message.PartitionKey, message.RowKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete message: {partitionKey}/{rowKey}", message.PartitionKey, message.RowKey);
                        }
                    }
                    
                    _logger.LogInformation("Batch {batchIndex}/{totalBatches} completed. Deleted {deletedCount}/{totalMessages}",
                        batchIndex + 1, totalBatches, deletedCount, messagesToDelete.Count);
                    
                    // Add small delay between batches to avoid overwhelming table storage
                    if (batchIndex < totalBatches - 1)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                    }
                }
                
                _logger.LogInformation("Completed cleanup: deleted {deletedCount} pre-generated messages", deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pre-generated messages cleanup");
                throw;
            }
        }

        private async Task IncrementHitCountAsync(PreGeneratedMessage message)
        {
            try
            {
                _logger.LogInformation("IncrementHitCountAsync START - CacheKey: {cacheKey}, Current HitCount: {currentCount}, ETag: {etag}", 
                    message.RowKey, message.HitCount, message.ETag);
                
                // Increment hit count and update last used timestamp
                var oldHitCount = message.HitCount;
                message.HitCount++;
                message.LastUsedAt = DateTime.UtcNow;
                
                _logger.LogInformation("IncrementHitCountAsync - About to update entity. CacheKey: {cacheKey}, Old HitCount: {oldCount}, New HitCount: {newCount}, LastUsedAt: {lastUsedAt}", 
                    message.RowKey, oldHitCount, message.HitCount, message.LastUsedAt);
                
                // Update the entity in the table using optimistic concurrency
                await _tableClient.UpdateEntityAsync(message, message.ETag, TableUpdateMode.Replace);
                
                _logger.LogInformation("IncrementHitCountAsync SUCCESS - CacheKey: {cacheKey}, Final HitCount: {count}, LastUsedAt: {lastUsedAt}", 
                    message.RowKey, message.HitCount, message.LastUsedAt);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Concurrency conflict - someone else updated the entity
                // This is acceptable for hit counting, we'll just log and continue
                _logger.LogWarning("IncrementHitCountAsync - Concurrency conflict (412) when updating hit count for message cacheKey: {cacheKey}. This is expected under high load.", 
                    message.RowKey);
            }
            catch (Exception ex)
            {
                // Don't throw - hit count tracking shouldn't break the main functionality
                _logger.LogError(ex, "IncrementHitCountAsync FAILED - CacheKey: {cacheKey}, Error: {error}", message.RowKey, ex.Message);
            }
        }

        /// <summary>
        /// Optimized version of NPC message generation with batching and parallel processing
        /// </summary>
        private async Task GenerateNPCMessagesFromDatabaseOptimizedAsync()
        {
            try
            {
                _logger.LogInformation("Loading NPC characters from database");
                
                // Get NPCs from database
                var npcTableClient = _tableServiceClient.GetTableClient(_storageOptions.NPCCharacterTableName);
                await npcTableClient.CreateIfNotExistsAsync();

                var npcs = new List<NPCCharacter>();
                try
                {
                    await foreach (var npc in npcTableClient.QueryAsync<NPCCharacter>(filter: "PartitionKey eq 'NPC'"))
                    {
                        npcs.Add(npc);
                    }
                }
                catch (Exception queryEx)
                {
                    _logger.LogError(queryEx, "Error querying NPC characters from table");
                    throw;
                }

                _logger.LogInformation("Found {count} NPC characters in database", npcs.Count);

                if (!npcs.Any())
                {
                    _logger.LogWarning("No NPC characters found in database. Consider seeding NPC data first.");
                    return;
                }

                // Get all tasks from the system to generate realistic messages
                var allTasks = _gameTaskService.GetTasks(rephrases: false);
                _logger.LogInformation("Found {count} tasks to generate messages with", allTasks.Count);

                // Reduced set of high-value message templates to minimize AI calls
                var priorityGameMessageTemplates = new[]
                {
                    // Task assignment messages (most important) - these will use real task data
                    "Ready for this? Task: {0}. {1}",
                    "New task: {0}. {1}",
                    
                    // Static messages that don't need task data
                    "Ready for your next Azure assignment?",
                    "Here's a task that will test your cloud skills.",
                    "Excellent work on that deployment!",
                    "Great job completing that task! Ready for the next challenge?",
                    "Let me guide you through this Azure service.",
                    "Don't worry, cloud computing can be tricky at first.",
                    "You're making excellent progress in your Azure adventure!",
                    "Your Azure skills are growing stronger!",
                    "Congratulations! You have completed all available tasks!",
                    "Hello! How can I help you today?"
                };

                var messagesToGenerate = new List<(string message, NPCCharacter npc)>();

                // Generate static messages (no task data needed)
                var staticMessages = priorityGameMessageTemplates.Where(t => !t.Contains("{0}")).ToArray();
                foreach (var message in staticMessages)
                {
                    foreach (var npc in npcs)
                    {
                        messagesToGenerate.Add((message, npc));
                    }
                }

                // Generate task-specific messages with real task data
                var taskMessageTemplates = priorityGameMessageTemplates.Where(t => t.Contains("{0}")).ToArray();
                foreach (var template in taskMessageTemplates)
                {
                    foreach (var task in allTasks.Take(5)) // Use first 5 tasks to limit combinations
                    {
                        foreach (var npc in npcs)
                        {
                            var messageText = string.Format(template, task.Name, task.Instruction);
                            messagesToGenerate.Add((messageText, npc));
                        }
                    }
                }

                var totalCombinations = messagesToGenerate.Count;
                _logger.LogInformation("Processing {totalCombinations} message-NPC combinations in optimized batches (including {staticCount} static and {taskCount} task-specific messages)", 
                    totalCombinations, staticMessages.Length * npcs.Count, taskMessageTemplates.Length * Math.Min(5, allTasks.Count) * npcs.Count);

                // Process in batches with parallel execution
                const int batchSize = 10; // Process 10 AI requests in parallel
                const int delayBetweenBatches = 2000; // 2 second delay between batches

                var processedCount = 0;
                var totalBatches = (int)Math.Ceiling((double)totalCombinations / batchSize);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var batch = messagesToGenerate.Skip(batchIndex * batchSize).Take(batchSize).ToList();
                    _logger.LogInformation("Processing batch {batchIndex}/{totalBatches} ({count} items)...", 
                        batchIndex + 1, totalBatches, batch.Count);

                    // Process batch in parallel with limited concurrency
                    var batchTasks = batch.Select(async item =>
                    {
                        try
                        {
                            await GenerateAndStoreNPCMessageAsync(item.message, item.npc.Age, item.npc.Gender, item.npc.Background);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate message for NPC: {npcName}, Message: {message}", item.npc.Name, item.message);
                            return false;
                        }
                    });

                    var results = await Task.WhenAll(batchTasks);
                    var successCount = results.Count(r => r);
                    processedCount += successCount;

                    _logger.LogInformation("Batch {batchIndex}/{totalBatches} completed: {successCount}/{totalItems} successful. Total processed: {processedCount}/{totalCombinations}",
                        batchIndex + 1, totalBatches, successCount, batch.Count, processedCount, totalCombinations);

                    // Add delay between batches to avoid overwhelming the AI service
                    if (batchIndex < totalBatches - 1) // Don't delay after the last batch
                    {
                        await Task.Delay(delayBetweenBatches);
                    }
                }

                _logger.LogInformation("Generated {count} NPC messages for {npcCount} NPCs using optimized batch processing with real task data", 
                    processedCount, npcs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating NPC messages from database (optimized)");
                throw;
            }
        }

        private static string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashedBytes).Replace("/", "_").Replace("+", "-").Replace("=", "");
        }
    }
}
