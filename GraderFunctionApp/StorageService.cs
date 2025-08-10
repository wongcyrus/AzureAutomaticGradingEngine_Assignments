using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text;

namespace GraderFunctionApp
{
    public class StorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly TableServiceClient _tableServiceClient;
        private readonly ILogger _logger;

        private const string TestResultsContainerName = "test-results";
        private const string PassTestTableName = "PassTests";
        private const string FailTestTableName = "FailTests";

        public StorageService(string connectionString, ILogger logger)
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            _tableServiceClient = new TableServiceClient(connectionString);
            _logger = logger;
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
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(TestResultsContainerName);
                
                // Create blob name: email_timestamp.xml
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var sanitizedEmail = SanitizeFileName(email);
                var blobName = $"{sanitizedEmail}_{timestamp}.xml";
                
                if (sanitizedEmail == "noemail")
                {
                    _logger.LogWarning("SaveTestResultXmlAsync: Using 'noemail' for email in blob name. Original email: '{email}' - this means no valid email was provided", email);
                }
                
                var blobClient = containerClient.GetBlobClient(blobName);
                
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                await blobClient.UploadAsync(stream, overwrite: true);
                
                _logger.LogInformation("Test result XML saved to blob: {blobName}", blobName);
                return blobName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test result XML for email: {email}", email);
                throw;
            }
        }

        public async Task SavePassTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults)
        {
            try
            {
                _logger.LogInformation("SavePassTestRecordAsync called with email: '{email}', {testCount} tests", email, testResults.Count);
                
                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("SavePassTestRecordAsync: email is null or empty - this should not happen");
                }
                
                var tableClient = _tableServiceClient.GetTableClient(PassTestTableName);
                await tableClient.CreateIfNotExistsAsync();

                var timestamp = DateTimeOffset.UtcNow;
                var partitionKey = SanitizeKey(email);
                
                if (partitionKey == "noemail")
                {
                    _logger.LogWarning("SavePassTestRecordAsync: Using 'noemail' for partition key. Original email: '{email}' - this means no valid email was provided", email);
                }
                
                foreach (var test in testResults.Where(t => t.Value == 1)) // Only passed tests
                {
                    var cleanTestName = CleanTestName(test.Key);
                    var rowKey = $"{cleanTestName}_{timestamp:yyyyMMddHHmmss}";
                    
                    if (cleanTestName == "invalidtest")
                    {
                        _logger.LogError("SavePassTestRecordAsync: Using 'invalidtest' for test name in row key. Original test name: '{testName}' - this indicates a problem with test name parsing", test.Key);
                    }
                    
                    var entity = new PassTestEntity
                    {
                        PartitionKey = partitionKey,
                        RowKey = rowKey,
                        Email = email,
                        TestName = test.Key,
                        PassedAt = timestamp,
                        ETag = Azure.ETag.All
                    };

                    _logger.LogDebug("SavePassTestRecordAsync: Saving pass test - PartitionKey: '{partitionKey}', RowKey: '{rowKey}', TestName: '{testName}'", 
                        partitionKey, rowKey, test.Key);
                    
                    await tableClient.UpsertEntityAsync(entity);
                }
                
                _logger.LogInformation("Pass test records saved for email: {email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save pass test records for email: {email}", email);
                throw;
            }
        }

        public async Task SaveFailTestRecordAsync(string email, string taskName, Dictionary<string, int> testResults)
        {
            try
            {
                _logger.LogInformation("SaveFailTestRecordAsync called with email: '{email}', {testCount} tests", email, testResults.Count);
                
                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning("SaveFailTestRecordAsync: email is null or empty - this should not happen");
                }
                
                var tableClient = _tableServiceClient.GetTableClient(FailTestTableName);
                await tableClient.CreateIfNotExistsAsync();

                var timestamp = DateTimeOffset.UtcNow;
                var partitionKey = SanitizeKey(email);
                
                if (partitionKey == "noemail")
                {
                    _logger.LogWarning("SaveFailTestRecordAsync: Using 'noemail' for partition key. Original email: '{email}' - this means no valid email was provided", email);
                }
                
                foreach (var test in testResults.Where(t => t.Value == 0)) // Only failed tests
                {
                    var cleanTestName = CleanTestName(test.Key);
                    var rowKey = $"{cleanTestName}_{timestamp:yyyyMMddHHmmss}";
                    
                    if (cleanTestName == "invalidtest")
                    {
                        _logger.LogError("SaveFailTestRecordAsync: Using 'invalidtest' for test name in row key. Original test name: '{testName}' - this indicates a problem with test name parsing", test.Key);
                    }
                    
                    var entity = new FailTestEntity
                    {
                        PartitionKey = partitionKey,
                        RowKey = rowKey,
                        Email = email,
                        TestName = test.Key,
                        FailedAt = timestamp,
                        ETag = Azure.ETag.All
                    };

                    _logger.LogDebug("SaveFailTestRecordAsync: Saving fail test - PartitionKey: '{partitionKey}', RowKey: '{rowKey}', TestName: '{testName}'", 
                        partitionKey, rowKey, test.Key);
                    
                    await tableClient.UpsertEntityAsync(entity);
                }
                
                _logger.LogInformation("Fail test records saved for email: {email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save fail test records for email: {email}", email);
                throw;
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
            var sanitized = input.Trim(); // Remove leading/trailing whitespace
            
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            
            // If sanitization resulted in empty string, use a safe fallback
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

            // Simply remove the first segment (namespace) from the test name
            // Pattern: AzureProjectTestLib.ClassName.MethodName -> ClassName.MethodName
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

            // Fallback: sanitize the full name (this should always work)
            var sanitizedName = SanitizeKey(fullTestName);
            if (!string.IsNullOrEmpty(sanitizedName) && sanitizedName != "noemail")
            {
                _logger.LogDebug("CleanTestName: '{fullTestName}' -> '{sanitizedName}' (sanitized full name)", fullTestName, sanitizedName);
                return sanitizedName;
            }

            // This should never happen, but if it does, we have a fallback
            _logger.LogError("CleanTestName: All strategies failed for '{fullTestName}' - using 'invalidtest'", fullTestName);
            return "invalidtest";
        }
    }

    public class PassTestEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string TestName { get; set; } = default!;
        public DateTimeOffset PassedAt { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public Azure.ETag ETag { get; set; }
    }

    public class FailTestEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string TestName { get; set; } = default!;
        public DateTimeOffset FailedAt { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public Azure.ETag ETag { get; set; }
    }
}
