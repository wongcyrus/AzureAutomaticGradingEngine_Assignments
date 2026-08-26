using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;

public class ClassDefinition : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static string OwnerPartition(string normalizedEmail) =>
        $"owner:{Hash(normalizedEmail)}";

    public static string StudentRowKey(string normalizedEmail) =>
        $"student:{Hash(normalizedEmail)}";

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
