using Azure;
using Azure.Data.Tables;
using System.Runtime.Serialization;

namespace GraderFunctionApp.Models
{
    [DataContract]
    public class GameState : ITableEntity
    {
        [DataMember] public string PartitionKey { get; set; } = string.Empty; // Email
        [DataMember] public string RowKey { get; set; } = string.Empty; // Game-NPC combination
        [DataMember] public DateTimeOffset? Timestamp { get; set; }
        [DataMember] public ETag ETag { get; set; }
        
        [DataMember] public string CurrentPhase { get; set; } = "TASK_ASSIGNED"; // TASK_ASSIGNED, READY_FOR_NEXT
        [DataMember] public string CurrentTaskName { get; set; } = string.Empty;
        [DataMember] public string CurrentTaskFilter { get; set; } = string.Empty;
        [DataMember] public int CurrentTaskReward { get; set; } = 0;
        [DataMember] public string LastMessage { get; set; } = string.Empty;
        [DataMember] public string ReportUrl { get; set; } = string.Empty;
        [DataMember] public string EasterEggUrl { get; set; } = string.Empty;
        [DataMember] public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        [DataMember] public int TotalScore { get; set; } = 0;
        [DataMember] public int CompletedTasks { get; set; } = 0;
        [DataMember] public string CompletedTasksList { get; set; } = string.Empty; // JSON array of completed task names
        [DataMember] public bool HasActiveTask { get; set; } = false;
    }
}
