using System.Xml;

namespace GraderFunctionApp.Services
{
    public static class TestResultParser
    {
        public static Dictionary<string, int> ParseNUnitTestResult(string rawXml)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(rawXml);
            return ParseNUnitTestResult(xmlDoc);
        }

        private static Dictionary<string, int> ParseNUnitTestResult(XmlDocument xmlDoc)
        {
            // More robust: select all test-case nodes anywhere in the tree
            var testCases = xmlDoc.SelectNodes("//test-case");
            var result = new Dictionary<string, int>();
            foreach (XmlNode node in testCases!)
            {
                var fullName = node?.Attributes?["fullname"]?.Value;
                if (string.IsNullOrEmpty(fullName)) continue;
                var passed = string.Equals(node?.Attributes?["result"]?.Value, "Passed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                result[fullName!] = passed;
            }

            return result;
        }
    }
}
