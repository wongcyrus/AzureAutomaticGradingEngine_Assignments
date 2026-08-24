using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;

public sealed class GameResetMarker : ITableEntity
{
    public const string ResetRowKey = "__reset_in_progress__";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = ResetRowKey;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
