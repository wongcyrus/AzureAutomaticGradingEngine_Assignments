namespace GraderFunctionApp.Configuration
{
    public class TestRunnerOptions
    {
        public const string SectionName = "TestRunner";

        public int TimeoutMinutes { get; set; } = 5;
        public string DefaultFilter { get; set; } = "test==AzureProjectTestLib";
        public string TestsWorkingDirectory { get; set; } = string.Empty;
    }
}
