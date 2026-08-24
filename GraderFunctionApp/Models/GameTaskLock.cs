using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;

public class GameTaskLock : ITableEntity
{
    public const string LockRowKey = "__active_task_lock__";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = LockRowKey;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Game { get; set; } = string.Empty;
    public string Npc { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
}
