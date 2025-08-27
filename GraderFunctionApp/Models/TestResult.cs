namespace GraderFunctionApp.Models
{
    public class TestResult
    {
        public string TestName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public int Score { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }

    public class TestSummary
    {
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int TotalScore { get; set; }
        public List<TestResult> Results { get; set; } = new();
        public TimeSpan ExecutionTime { get; set; }
    }
}
