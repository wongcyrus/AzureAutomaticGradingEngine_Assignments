using Azure.Data.Tables;

namespace GraderFunctionApp.Models
{
    public class FailTestEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string TestName { get; set; } = default!;
        public string TaskName { get; set; } = default!; // Name of the failed task
        public string AssignedByNPC { get; set; } = default!; // NPC who assigned this task
        public DateTimeOffset FailedAt { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public Azure.ETag ETag { get; set; }
    }
}
