using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Interfaces
{
    public interface ITestRunner
    {
        Task<string?> RunUnitTestProcessAsync(ILogger log, string credentials, string trace, string filter);
    }
}
