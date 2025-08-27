namespace GraderFunctionApp.Interfaces
{
    public interface ITestResultParser
    {
        Dictionary<string, int> ParseNUnitTestResult(string xml);
    }
}
