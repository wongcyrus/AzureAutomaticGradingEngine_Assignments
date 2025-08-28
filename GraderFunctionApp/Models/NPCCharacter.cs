using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models
{
    public class NPCCharacter : ITableEntity
    {
        public string PartitionKey { get; set; } = "NPC"; // Fixed partition for all NPCs
        public string RowKey { get; set; } = default!; // NPC Name
        public string Name { get; set; } = default!;
        public int Age { get; set; }
        public string Gender { get; set; } = default!;
        public string Background { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
