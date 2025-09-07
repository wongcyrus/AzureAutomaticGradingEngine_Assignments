namespace GraderFunctionApp.Models
{
    public class PreGeneratedMessageStats
    {
        public int TotalMessages { get; set; }
        public int TotalHits { get; set; }
        public int InstructionMessages { get; set; }
        public int InstructionHits { get; set; }
        public int NPCMessages { get; set; }
        public int NPCHits { get; set; }
        public int UnusedMessages { get; set; }
        public double OverallHitRate => TotalMessages > 0 ? (double)TotalHits / TotalMessages : 0;
        public double InstructionHitRate => InstructionMessages > 0 ? (double)InstructionHits / InstructionMessages : 0;
        public double NPCHitRate => NPCMessages > 0 ? (double)NPCHits / NPCMessages : 0;
        public PreGeneratedMessage? MostUsedMessage { get; set; }
        public PreGeneratedMessage? LeastUsedMessage { get; set; }
        public DateTime? LastRefreshTime { get; set; }
    }
}
