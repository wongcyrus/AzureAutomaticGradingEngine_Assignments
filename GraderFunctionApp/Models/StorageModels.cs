namespace GraderFunctionApp.Models
{
    public class TestResultRecord
    {
        public required string Email { get; set; }
        public required string TaskName { get; set; }
        public required Dictionary<string, int> Results { get; set; }
    }
}
