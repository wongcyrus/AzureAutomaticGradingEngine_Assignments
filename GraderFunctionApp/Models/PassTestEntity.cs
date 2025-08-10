using Azure.Data.Tables;

namespace GraderFunctionApp.Models
{
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
}
