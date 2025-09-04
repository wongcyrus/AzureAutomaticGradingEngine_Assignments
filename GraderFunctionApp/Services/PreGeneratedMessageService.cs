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
                var npcCharacteristics = JsonConvert.SerializeObject(new { age, gender, background });
                var combinedKey = $"{originalMessage}|{npcCharacteristics}";
                var messageHash = ComputeHash(combinedKey);

                var response = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("npc", messageHash);

                if (response.HasValue && response.Value != null)
                {
                    var preGeneratedMessage = response.Value;
                    _logger.LogDebug("Found pre-generated NPC message for hash: {hash}", messageHash);
                    return preGeneratedMessage?.GeneratedMessage;
                }

                _logger.LogDebug("No pre-generated NPC message found for hash: {hash}", messageHash);
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

                // Generate messages sequentially to avoid overwhelming the AI service
                _logger.LogInformation("Generating instruction messages from tasks...");
                await GenerateInstructionMessagesFromTasksAsync();
                
                _logger.LogInformation("Generating NPC messages from database...");
                await GenerateNPCMessagesFromDatabaseAsync();

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

                var generatedCount = 0;
                foreach (var task in allTasks)
                {
                    if (!string.IsNullOrEmpty(task.Instruction))
                    {
                        try
                        {
                            await GenerateAndStoreInstructionAsync(task.Instruction);
                            generatedCount++;
                            
                            // Add delay to avoid rate limiting
                            await Task.Delay(200);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate instruction for task: {taskName}", task.Name);
                        }
                    }
                }

                _logger.LogInformation("Processed {count} task instructions for pre-generation", generatedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating instruction messages from tasks");
                throw;
            }
        }

        private async Task GenerateNPCMessagesFromDatabaseAsync()
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

                // Generate contextual messages based on actual game scenarios
                // These are the types of messages NPCs actually say during gameplay
                var gameContextMessages = new[]
                {
                    // Task assignment messages
                    "I have a new challenge for you to complete.",
                    "Ready for your next Azure assignment?",
                    "Here's a task that will test your cloud skills.",
                    "Time to put your Azure knowledge to work!",
                    "Here's your next challenge: {0}. {1}",
                    "Ready for this? Task: {0}. {1}",
                    "Time for a new adventure: {0}. {1}",
                    "Let's tackle this together: {0}. {1}",
                    
                    // Task completion and feedback
                    "Excellent work on that deployment!",
                    "Great job completing that task! Ready for the next challenge?",
                    "Congratulations on completing the previous task!",
                    "Your configuration looks perfect!",
                    
                    // Guidance and encouragement
                    "Let me guide you through this Azure service.",
                    "Don't worry, cloud computing can be tricky at first.",
                    "Here's a tip that will help you succeed.",
                    "Remember, practice makes perfect in the cloud.",
                    
                    // Progress acknowledgment
                    "You're making excellent progress in your Azure adventure!",
                    "Your understanding of Azure is improving rapidly.",
                    "Your Azure skills are growing stronger!",
                    "You're becoming a true Azure expert!",
                    
                    // Active task reminders
                    "Your current task: {0}. Complete it and chat with me again for grading!",
                    "Don't forget about your active task: {0}. Finish it up!",
                    "You still have work to do on: {0}. Let's get it done!",
                    "Focus on completing: {0}. I'll be here when you're ready for grading!",
                    "Remember your current assignment: {0}. Time to finish it!",
                    "Complete your current task and chat with me again for grading!",
                    "Focus on your current assignment first.",
                    "Finish up your current work, then we can talk!",
                    
                    // Busy with other NPC messages
                    "Hello there! I see you're working with another NPC on a task. You should complete that first before I can help you with anything new.",
                    "Hi! Looks like another NPC has given you something to work on. Focus on that task first, then come back to see me!",
                    "Greetings! I notice you have an active task with another NPC. One task at a time - finish that one first!",
                    "Nice to meet you! But I can see you're already working on something. One step at a time!",
                    "Hi there! You look busy with your current task. Come back when you're ready for something new!",
                    "Hello! I'd love to help, but you should finish your current assignment first. Good luck!",
                    "Greetings! Focus on your current task for now. I'll be here when you're ready for the next challenge!",
                    "Hey! Looks like you have your hands full already. Complete your current work first!",
                    
                    // Cooldown messages
                    "I just gave you a task recently! Come back later for a new challenge.",
                    "You need to wait a bit more before I can give you another task.",
                    "I'm still preparing your next challenge. Return later!",
                    "Give me some time to prepare something new for you.",
                    "You're eager! But wait a bit before I assign another task.",
                    "Let me get ready for your next assignment. Check back later!",
                    "Patience! Your next challenge will be ready soon.",
                    
                    // Completion messages
                    "Congratulations! You have completed all available tasks!",
                    "Amazing work! You've mastered all the challenges I have for you.",
                    "Incredible! You've completed everything. You're a true Azure expert!",
                    "Fantastic job! You've conquered all my tasks. Well done!",
                    "Outstanding! You've finished all challenges. You should be proud!",
                    
                    // Generic greetings
                    "Hello! How can I help you today?",
                    "Greetings! What brings you to see me?",
                    "Hi there! Ready for some Azure learning?",
                    "Welcome! Let's see what we can accomplish together.",
                    "Good to see you! What would you like to work on?"
                };

                var generatedCount = 0;
                foreach (var message in gameContextMessages)
                {
                    foreach (var npc in npcs)
                    {
                        try
                        {
                            await GenerateAndStoreNPCMessageAsync(message, npc.Age, npc.Gender, npc.Background);
                            generatedCount++;
                            
                            // Add delay to avoid rate limiting
                            await Task.Delay(200);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate NPC message for NPC: {npcName}", npc.Name);
                        }
                    }
                }

                // Also generate personalized versions of dynamic response templates used in GameTaskFunction
                var dynamicResponseTemplates = new[]
                {
                    "Hello there! I see you're working with {0} on a task. You should complete that first before I can help you with anything new.",
                    "Hi! Looks like {0} has given you something to work on. Focus on that task first, then come back to see me!",
                    "Greetings! I notice you have an active task with {0}. One task at a time - finish that one first!",
                    "Your current task: {0}. Complete it and chat with me again for grading!",
                    "I just gave you a task recently! Come back in {0} minutes for a new challenge.",
                    "You need to wait {0} more minutes before I can give you another task.",
                    "Give me {0} more minutes to prepare something new for you.",
                    "New task: {0}. Let's get started with this challenge!"
                };

                // Generate personalized versions with placeholder values
                foreach (var template in dynamicResponseTemplates)
                {
                    foreach (var npc in npcs)
                    {
                        try
                        {
                            // Replace placeholders with generic values for pre-generation
                            var message = template.Contains("{0}") 
                                ? string.Format(template, "another NPC") 
                                : template;
                                
                            await GenerateAndStoreNPCMessageAsync(message, npc.Age, npc.Gender, npc.Background);
                            generatedCount++;
                            
                            // Add delay to avoid rate limiting
                            await Task.Delay(150);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate dynamic template message for NPC: {npcName}", npc.Name);
                        }
                    }
                }

                _logger.LogInformation("Generated {count} NPC messages for {npcCount} NPCs", generatedCount, npcs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating NPC messages from database");
                throw;
            }
        }

        private async Task GenerateAndStoreNPCMessageAsync(string originalMessage, int age, string gender, string background)
        {
            try
            {
                var npcCharacteristics = JsonConvert.SerializeObject(new { age, gender, background });
                var combinedKey = $"{originalMessage}|{npcCharacteristics}";
                var messageHash = ComputeHash(combinedKey);

                // Check if already exists
                var existingResponse = await _tableClient.GetEntityIfExistsAsync<PreGeneratedMessage>("npc", messageHash);
                if (existingResponse.HasValue && existingResponse.Value != null)
                {
                    _logger.LogDebug("NPC message already exists for hash: {hash}", messageHash);
                    return;
                }

                // Generate new message using AI service
                var generatedMessage = await _aiService.PersonalizeNPCMessageAsync(originalMessage, age, gender, background);

                if (!string.IsNullOrEmpty(generatedMessage) && generatedMessage != $"Tek, {originalMessage}")
                {
                    var preGeneratedMessage = new PreGeneratedMessage
                    {
                        PartitionKey = "npc",
                        RowKey = messageHash,
                        OriginalMessage = originalMessage,
                        GeneratedMessage = generatedMessage,
                        MessageType = "npc",
                        NPCCharacteristics = npcCharacteristics,
                        GeneratedAt = DateTime.UtcNow
                    };

                    await _tableClient.UpsertEntityAsync(preGeneratedMessage);
                    _logger.LogDebug("Stored pre-generated NPC message for hash: {hash}", messageHash);
                }
                else
                {
                    _logger.LogDebug("Skipping NPC message - AI returned empty or default response for: {message}", originalMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating NPC message for: {message}", originalMessage);
                // Don't rethrow - let the batch continue processing
            }
        }

        private async Task GenerateAndStoreInstructionAsync(string originalInstruction)
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating instruction message for: {instruction}", originalInstruction);
                // Don't rethrow - let the batch continue processing
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
