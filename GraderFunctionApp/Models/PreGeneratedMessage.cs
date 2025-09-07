using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models
{
    public class PreGeneratedMessage : ITableEntity
    {
        public string PartitionKey { get; set; } = default!; // Message type: "instruction" or "npc"
        public string RowKey { get; set; } = default!; // Hash of original message or unique identifier
        public string OriginalMessage { get; set; } = default!;
        public string GeneratedMessage { get; set; } = default!;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public string MessageType { get; set; } = default!; // "instruction" or "npc"
        public string? NPCCharacteristics { get; set; } // For NPC messages: JSON string with age, gender, background
        public int HitCount { get; set; } = 0; // Number of times this pre-generated message has been used
        public DateTime? LastUsedAt { get; set; } // When this message was last used
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
