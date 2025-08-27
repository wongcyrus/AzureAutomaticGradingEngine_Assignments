using System.Xml;
using Microsoft.Extensions.Logging;
using GraderFunctionApp.Interfaces;

namespace GraderFunctionApp.Services
{
    public class TestResultParser : ITestResultParser
    {
        private readonly ILogger<TestResultParser> _logger;

        public TestResultParser(ILogger<TestResultParser> logger)
        {
            _logger = logger;
        }

        public Dictionary<string, int> ParseNUnitTestResult(string xml)
        {
            var results = new Dictionary<string, int>();
            
            if (string.IsNullOrEmpty(xml))
            {
                _logger.LogWarning("ParseNUnitTestResult received null or empty XML");
                return results;
            }

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                var testCases = doc.SelectNodes("//test-case");
                if (testCases == null)
                {
                    _logger.LogWarning("No test-case nodes found in XML");
                    return results;
                }

                foreach (XmlNode testCase in testCases)
                {
                    var nameAttr = testCase.Attributes?["name"];
                    var resultAttr = testCase.Attributes?["result"];

                    if (nameAttr?.Value != null && resultAttr?.Value != null)
                    {
                        var testName = nameAttr.Value;
                        var isPassed = string.Equals(resultAttr.Value, "Passed", StringComparison.OrdinalIgnoreCase);
                        results[testName] = isPassed ? 1 : 0;
                    }
                }

                _logger.LogInformation("Parsed {count} test results from XML", results.Count);
            }
            catch (XmlException ex)
            {
                _logger.LogError(ex, "Failed to parse XML test results");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while parsing test results");
            }

            return results;
        }
    }
}
