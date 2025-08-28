using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models
{
    public class EasterEgg : ITableEntity
    {
        public string PartitionKey { get; set; } = "EasterEgg"; // Fixed partition
        public string RowKey { get; set; } = default!; // Unique ID
        public string Type { get; set; } = default!; // "Pass" or "Fail"
        public string Link { get; set; } = default!; // Popup URL
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
