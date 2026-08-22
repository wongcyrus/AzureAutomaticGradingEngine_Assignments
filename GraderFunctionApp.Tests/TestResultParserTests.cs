using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraderFunctionApp.Tests;

public class TestResultParserTests
{
    private readonly TestResultParser parser = new(NullLogger<TestResultParser>.Instance);

    [Test]
    public void ParseNUnitTestResult_MapsPassedAndNonPassedResults()
    {
        const string xml = """
            <test-run>
              <test-case name="PassingTest" result="Passed" />
              <test-case name="FailingTest" result="Failed" />
              <test-case name="ErrorTest" result="Error" />
            </test-run>
            """;

        var results = parser.ParseNUnitTestResult(xml);

        Assert.That(results, Is.EqualTo(new Dictionary<string, int>
        {
            ["PassingTest"] = 1,
            ["FailingTest"] = 0,
            ["ErrorTest"] = 0
        }));
    }

    [TestCase("")]
    [TestCase("not XML")]
    [TestCase("<test-run />")]
    public void ParseNUnitTestResult_InvalidOrEmptyInput_ReturnsNoResults(string xml)
    {
        Assert.That(parser.ParseNUnitTestResult(xml), Is.Empty);
    }

    [Test]
    public void ParseNUnitTestResult_IgnoresCasesWithoutRequiredAttributes()
    {
        const string xml = """
            <test-run>
              <test-case name="MissingResult" />
              <test-case result="Passed" />
              <test-case name="Complete" result="passed" />
            </test-run>
            """;

        var results = parser.ParseNUnitTestResult(xml);

        Assert.That(results, Is.EqualTo(new Dictionary<string, int>
        {
            ["Complete"] = 1
        }));
    }
}
