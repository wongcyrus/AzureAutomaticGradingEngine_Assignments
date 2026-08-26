using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;

public class ClassMembership : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
}
