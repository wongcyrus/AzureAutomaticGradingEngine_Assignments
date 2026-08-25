using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using GraderFunctionApp.Models;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Configuration;

namespace GraderFunctionApp.Services
{
    public class StorageService : IStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly TableServiceClient _tableServiceClient;
        private readonly ILogger<StorageService> _logger;
        private readonly StorageOptions _options;

        public StorageService(string connectionString, ILogger<StorageService> logger, IOptions<StorageOptions> options)
            : this(
                new BlobServiceClient(connectionString),
                new TableServiceClient(connectionString),
                logger,
                options)
        {
        }

        public StorageService(
            BlobServiceClient blobServiceClient,
            TableServiceClient tableServiceClient,
            ILogger<StorageService> logger,
            IOptions<StorageOptions> options)
        {
            _blobServiceClient = blobServiceClient;
            _tableServiceClient = tableServiceClient;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<string> SaveTestResultXmlAsync(string email, string xml)
        {
            try
            {
                _logger.LogInformation("SaveTestResultXmlAsync called with email: '{email}', xml length: {xmlLength}", email, xml?.Length ?? 0);

                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("SaveTestResultXmlAsync: email is null or empty - this should not happen");
                }

                if (string.IsNullOrEmpty(xml))
                {
                    _logger.LogWarning("SaveTestResultXmlAsync: xml is null or empty - this should not happen");
                    xml = "<empty/>";
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_options.TestResultsContainerName);
                await containerClient.CreateIfNotExistsAsync();

                // Create blob name: email_timestamp.xml
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                var sanitizedEmail = SanitizeFileName(email);
                var blobName = $"{sanitizedEmail}_{timestamp}_{Guid.NewGuid():N}.xml";

                if (sanitizedEmail == "noemail")
                {
                    _logger.LogWarning("SaveTestResultXmlAsync: Using 'noemail' for email in blob name. Original email: '{email}' - this means no valid email was provided", email);
                }

                var blobClient = containerClient.GetBlobClient(blobName);

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "text/xml"
                };
                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders
                });

                _logger.LogInformation("Test result XML saved to blob: {blobName}", blobName);
                return blobName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test result XML for email: {email}", email);
                throw;
            }
        }

        public async Task SavePassTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC)
        {
            try
            {
                _logger.LogInformation("SavePassTestRecordAsync called with email: '{email}', {testCount} tests, NPC: '{npc}'", email, testResults.Count, assignedByNPC);
                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("SavePassTestRecordAsync: email is null or empty - this should not happen");
                }
                var tableClient = await PrepareTableAsync(_options.PassTestTableName);
                var timestamp = DateTimeOffset.UtcNow;
                var partitionKey = SanitizeKey(email);
                LogIfNoEmail(partitionKey, email, nameof(SavePassTestRecordAsync));
                
                foreach (var test in testResults.Where(static t => t.Value >= 0))
                {
                    await SaveTestEntityAsync(tableClient, partitionKey, email, test.Key, timestamp, true, test.Value, taskName, assignedByNPC);
                }
                _logger.LogInformation("Pass test records saved for email: {email}, NPC: {npc}", email, assignedByNPC);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save pass test records for email: {email}", email);
                throw;
            }
        }

        public async Task SaveFailTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults, string assignedByNPC)
        {
            try
            {
                _logger.LogInformation("SaveFailTestRecordAsync called with email: '{email}', {testCount} tests, NPC: '{npc}'", email, testResults.Count, assignedByNPC);
                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("SaveFailTestRecordAsync: email is null or empty - this should not happen");
                }
                var tableClient = await PrepareTableAsync(_options.FailTestTableName);
                var timestamp = DateTimeOffset.UtcNow;
                var partitionKey = SanitizeKey(email);
                LogIfNoEmail(partitionKey, email, nameof(SaveFailTestRecordAsync));
                
                foreach (var test in testResults.Where(static t => t.Value == 0))
                {
                    await SaveTestEntityAsync(tableClient, partitionKey, email, test.Key, timestamp, false, 0, taskName, assignedByNPC);
                }
                _logger.LogInformation("Fail test records saved for email: {email}, NPC: {npc}", email, assignedByNPC);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save fail test records for email: {email}", email);
                throw;
            }
        }

        public async Task<List<(string Name, int Mark)>> GetPassedTasksAsync(string email)
        {
            try
            {
                _logger.LogInformation("GetPassedTasksAsync called with email: {email}", email);

                var tableClient = _tableServiceClient.GetTableClient(_options.PassTestTableName);
                var partitionKey = SanitizeKey(email);

                var queryResults = tableClient.QueryAsync<PassTestEntity>(e => e.PartitionKey == partitionKey);
                var passedTasks = new List<(string Name, int Mark)>();

                await foreach (var entity in queryResults)
                {
                    passedTasks.Add((entity.TestName, entity.Mark));
                }

                _logger.LogInformation("Fetched {count} passed tasks for email: {email}", passedTasks.Count, email);
                return passedTasks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch passed tasks for email: {email}", email);
                throw;
            }
        }

        public async Task<List<FailTestEntity>> GetFailedTestsAsync(string email)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient(
                    _options.FailTestTableName);
                var partitionKey = SanitizeKey(email);
                var failedTests = new List<FailTestEntity>();

                await foreach (var entity in tableClient.QueryAsync<FailTestEntity>(
                                   entity => entity.PartitionKey == partitionKey))
                {
                    failedTests.Add(entity);
                }

                return failedTests;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to fetch failed tests for email: {email}",
                    email);
                throw;
            }
        }

        public async Task<List<string>> GetCompletedTaskNamesAsync(string email)
        {
            try
            {
                _logger.LogInformation("GetCompletedTaskNamesAsync called with email: {email}", email);

                var tableClient = _tableServiceClient.GetTableClient(_options.PassTestTableName);
                var partitionKey = SanitizeKey(email);

                var queryResults = tableClient.QueryAsync<PassTestEntity>(e => e.PartitionKey == partitionKey);
                var completedTaskNames = new HashSet<string>();

                await foreach (var entity in queryResults)
                {
                    if (!string.IsNullOrEmpty(entity.TaskName))
                    {
                        completedTaskNames.Add(entity.TaskName);
                    }
                }

                var result = completedTaskNames.ToList();
                _logger.LogInformation("Fetched {count} completed task names for email: {email}", result.Count, email);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch completed task names for email: {email}", email);
                throw;
            }
        }

        public Task<int> DeletePassedTasksAsync(string email)
        {
            return DeletePartitionEntitiesAsync(
                _options.PassTestTableName,
                SanitizeKey(email));
        }

        private async Task<TableClient> PrepareTableAsync(string tableName)
        {
            var tableClient = _tableServiceClient.GetTableClient(tableName);
            await tableClient.CreateIfNotExistsAsync();
            return tableClient;
        }

        private void LogIfNoEmail(string partitionKey, string email, string methodName)
        {
            if (partitionKey == "noemail")
            {
                _logger.LogWarning($"{methodName}: Using 'noemail' for partition key. Original email: '{{email}}' - this means no valid email was provided", email);
            }
        }

        private async Task SaveTestEntityAsync(TableClient tableClient, string partitionKey, string email, string testName, DateTimeOffset timestamp, bool isPass, int mark = 0, string taskName = "", string assignedByNPC = "")
        {
            var cleanTestName = CleanTestName(testName);
            var rowKey = isPass
                ? cleanTestName
                : $"{cleanTestName}_{timestamp:yyyyMMddHHmmssfffffff}_{Guid.NewGuid():N}";
            
            if (cleanTestName == "invalidtest")
            {
                _logger.LogError($"{(isPass ? nameof(SavePassTestRecordAsync) : nameof(SaveFailTestRecordAsync))}: Using 'invalidtest' for test name in row key. Original test name: '{{testName}}' - this indicates a problem with test name parsing", testName);
            }

            if (isPass)
            {
                // Check if record already exists
                try
                {
                    var existing = await tableClient.GetEntityAsync<PassTestEntity>(partitionKey, rowKey);
                    if (existing != null)
                    {
                        _logger.LogInformation("Pass record already exists for PartitionKey: {partitionKey}, RowKey: {rowKey}. Skipping insert.", partitionKey, rowKey);
                        return;
                    }
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Not found, proceed to insert
                }
            }

            ITableEntity entity = isPass
            ? new PassTestEntity { 
                PartitionKey = partitionKey, 
                RowKey = rowKey, 
                Email = email, 
                TestName = testName, 
                TaskName = taskName,
                AssignedByNPC = assignedByNPC,
                PassedAt = timestamp, 
                Mark = mark, 
                ETag = ETag.All 
            }
            : new FailTestEntity { 
                PartitionKey = partitionKey, 
                RowKey = rowKey, 
                Email = email, 
                TestName = testName, 
                TaskName = taskName,
                AssignedByNPC = assignedByNPC,
                FailedAt = timestamp, 
                ETag = ETag.All 
            };

            _logger.LogInformation($"{(isPass ? nameof(SavePassTestRecordAsync) : nameof(SaveFailTestRecordAsync))}: Saving test - PartitionKey: '{{partitionKey}}', RowKey: '{{rowKey}}', TestName: '{{testName}}', Mark: {{mark}}, NPC: '{{npc}}'", partitionKey, rowKey, testName, mark, assignedByNPC);
            if (isPass)
            {
                await EnsureResetNotInProgressAsync(email);
                await tableClient.UpsertEntityAsync(entity);
            }
            else
            {
                await tableClient.AddEntityAsync(entity);
            }
        }

        private async Task<int> DeletePartitionEntitiesAsync(
            string tableName,
            string partitionKey)
        {
            var tableClient = _tableServiceClient.GetTableClient(tableName);
            var deletedCount = 0;
            var filter = TableClient.CreateQueryFilter(
                $"PartitionKey eq {partitionKey}");

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var entities = new List<TableEntity>();
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(
                                   filter: filter))
                {
                    entities.Add(entity);
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
                    await tableClient.SubmitTransactionAsync(actions);
                }

                deletedCount += entities.Count;
            }

            throw new InvalidOperationException(
                $"Could not clear the {tableName} partition because records are still being created.");
        }

        private async Task EnsureResetNotInProgressAsync(string email)
        {
            var gameStates = _tableServiceClient.GetTableClient("GameStates");
            var marker = await gameStates.GetEntityIfExistsAsync<GameResetMarker>(
                email,
                GameResetMarker.ResetRowKey);
            if (marker.HasValue)
            {
                throw new InvalidOperationException(
                    "Game progress is currently being reset.");
            }
        }

        public async Task<string?> GenerateTestResultSasUrlAsync(string blobName)
        {
            try
            {
                if (string.IsNullOrEmpty(blobName))
                {
                    _logger.LogWarning("GenerateTestResultSasUrlAsync: blobName is null or empty");
                    return null;
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_options.TestResultsContainerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Check if blob exists
                var exists = await blobClient.ExistsAsync();
                if (!exists.Value)
                {
                    _logger.LogWarning("GenerateTestResultSasUrlAsync: Blob {blobName} does not exist", blobName);
                    return null;
                }

                // Generate SAS URL with 1 hour expiry
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _options.TestResultsContainerName,
                    BlobName = blobName,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
                _logger.LogInformation("Generated SAS URL for blob: {blobName}", blobName);
                return sasUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate SAS URL for blob: {blobName}", blobName);
                return null;
            }
        }

        private string SanitizeFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                _logger.LogWarning("SanitizeFileName received null or empty input - this means no email was provided. Using 'noemail'");
                return "noemail";
            }

            if (input == "Anonymous")
            {
                _logger.LogDebug("SanitizeFileName received 'Anonymous' - this means no email was extracted from trace or POST request");
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        }

        private string SanitizeKey(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                _logger.LogWarning("SanitizeKey received null or empty input - this means no email was provided. Using 'noemail'");
                return "noemail";
            }

            if (input == "Anonymous")
            {
                _logger.LogDebug("SanitizeKey received 'Anonymous' - this means no email was extracted from trace or POST request");
            }

            // Azure Table keys cannot contain certain characters
            var invalidChars = new[] { '/', '\\', '#', '?', '\t', '\n', '\r' };
            var sanitized = input.Trim();

            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            if (string.IsNullOrEmpty(sanitized))
            {
                _logger.LogWarning("SanitizeKey resulted in empty string after sanitization. Original input: '{input}'. This means the input contained only invalid characters. Using 'sanitized'", input);
                return "sanitized";
            }

            return sanitized;
        }

        private string CleanTestName(string fullTestName)
        {
            if (string.IsNullOrEmpty(fullTestName))
            {
                _logger.LogError("CleanTestName received null or empty test name - this should never happen. Using 'invalidtest'");
                return "invalidtest";
            }

            var firstDotIndex = fullTestName.IndexOf('.');
            if (firstDotIndex >= 0 && firstDotIndex < fullTestName.Length - 1)
            {
                var withoutNamespace = fullTestName.Substring(firstDotIndex + 1);
                if (!string.IsNullOrEmpty(withoutNamespace))
                {
                    var cleanName = SanitizeKey(withoutNamespace);
                    if (!string.IsNullOrEmpty(cleanName) && cleanName != "noemail")
                    {
                        _logger.LogDebug("CleanTestName: '{fullTestName}' -> '{cleanName}' (removed first segment)", fullTestName, cleanName);
                        return cleanName;
                    }
                }
            }

            var sanitizedName = SanitizeKey(fullTestName);
            if (!string.IsNullOrEmpty(sanitizedName) && sanitizedName != "noemail")
            {
                _logger.LogDebug("CleanTestName: '{fullTestName}' -> '{sanitizedName}' (sanitized full name)", fullTestName, sanitizedName);
                return sanitizedName;
            }

            _logger.LogError("CleanTestName: All strategies failed for '{fullTestName}' - using 'invalidtest'", fullTestName);
            return "invalidtest";
        }

        public async Task<string?> GetSubscriptionIdAsync(string email)
        {
            _logger.LogInformation("Getting registered subscription for email: '{email}'", email);

            var normalizedEmail = SubscriptionRegistration.NormalizeEmail(email);
            var tableClient = _tableServiceClient.GetTableClient(
                _options.SubscriptionRegistrationsTableName);
            await tableClient.CreateIfNotExistsAsync();

            var response =
                await tableClient.GetEntityIfExistsAsync<SubscriptionRegistration>(
                    SubscriptionRegistration.Partition,
                    SubscriptionRegistration.EmailRowKey(normalizedEmail));
            if (response.HasValue && response.Value is { } registration)
            {
                if (registration.PartitionKey != SubscriptionRegistration.Partition ||
                    registration.RowKey !=
                        SubscriptionRegistration.EmailRowKey(normalizedEmail) ||
                    registration.IndexKind !=
                        SubscriptionRegistration.EmailIndexKind ||
                    registration.Email != normalizedEmail ||
                    !Guid.TryParse(registration.SubscriptionId, out var subscriptionId) ||
                    registration.SubscriptionId !=
                        SubscriptionRegistration.NormalizeSubscriptionId(subscriptionId))
                {
                    _logger.LogError(
                        "Subscription registration email index is inconsistent for {emailHash}",
                        SubscriptionRegistration.EmailRowKey(normalizedEmail));
                    return null;
                }

                return registration.SubscriptionId;
            }

            _logger.LogWarning("No subscription registered for email: '{email}'", email);
            return null;
        }

        public async Task<string?> GetLastTaskNPCAsync(string email)
        {
            try
            {
                _logger.LogInformation("GetLastTaskNPCAsync called with email: '{email}'", email);

                var tableClient = _tableServiceClient.GetTableClient(_options.PassTestTableName);
                await tableClient.CreateIfNotExistsAsync();

                var partitionKey = SanitizeKey(email);
                
                // Get the most recent completed task for this user
                var query = tableClient.QueryAsync<PassTestEntity>(
                    filter: $"PartitionKey eq '{partitionKey}'");

                PassTestEntity? mostRecentTask = null;
                await foreach (var entity in query)
                {
                    if (mostRecentTask == null || entity.PassedAt > mostRecentTask.PassedAt)
                    {
                        mostRecentTask = entity;
                    }
                }

                var lastNPC = mostRecentTask?.AssignedByNPC;
                _logger.LogInformation("Last task NPC for {email}: {npc}", email, lastNPC ?? "none");
                return lastNPC;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last task NPC for email: '{email}'", email);
                return null;
            }
        }

        public async Task<string?> GetRandomEasterEggAsync(string type)
        {
            try
            {
                var tableClient = _tableServiceClient.GetTableClient("EasterEgg");
                await tableClient.CreateIfNotExistsAsync();

                var query = tableClient.QueryAsync<EasterEgg>(filter: $"Type eq '{type}'");
                var easterEggs = new List<EasterEgg>();
                
                await foreach (var egg in query)
                {
                    easterEggs.Add(egg);
                }

                if (easterEggs.Count == 0)
                {
                    return null;
                }

                var selectedEgg = easterEggs[Random.Shared.Next(easterEggs.Count)];
                return selectedEgg.Link;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random easter egg for type: {type}", type);
                return null;
            }
        }

        public async Task<NPCCharacter?> GetNPCCharacterAsync(string npcName)
        {
            try
            {
                _logger.LogInformation("GetNPCCharacterAsync called with npcName: '{npcName}'", npcName);

                var tableClient = _tableServiceClient.GetTableClient(_options.NPCCharacterTableName);
                await tableClient.CreateIfNotExistsAsync();

                var response = await tableClient.GetEntityIfExistsAsync<NPCCharacter>("NPC", npcName);
                
                if (response.HasValue)
                {
                    _logger.LogInformation("Found NPC character: '{npcName}'", npcName);
                    return response.Value;
                }
                else
                {
                    _logger.LogWarning("No character data found for NPC: '{npcName}'", npcName);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving NPC character for: '{npcName}'", npcName);
                return null;
            }
        }
    }
}
