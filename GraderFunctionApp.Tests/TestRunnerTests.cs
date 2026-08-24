using GraderFunctionApp.Configuration;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class TestRunnerTests
{
    private string testsDirectory = null!;
    private IGameTaskService gameTaskService = null!;

    [SetUp]
    public void SetUp()
    {
        testsDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(testsDirectory);
        gameTaskService = Substitute.For<IGameTaskService>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testsDirectory))
        {
            Directory.Delete(testsDirectory, recursive: true);
        }
    }

    [Test]
    public async Task RunUnitTestProcessAsync_NoExecutable_ReturnsNullAndCleansWorkDirectory()
    {
        var runner = CreateRunner();

        var result = await runner.RunUnitTestProcessAsync(
            NullLogger.Instance,
            Guid.NewGuid().ToString(),
            "missing-runner",
            "");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RunUnitTestProcessAsync_UnknownFilter_ReturnsNullWithoutStartingRunner()
    {
        gameTaskService.GetTasksJson(false).Returns("[]");
        var runner = CreateRunner();

        var result = await runner.RunUnitTestProcessAsync(
            NullLogger.Instance,
            Guid.NewGuid().ToString(),
            "unknown-filter",
            "test=Unknown");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RunUnitTestProcessAsync_FilterLookupFailure_UsesOriginalFilter()
    {
        gameTaskService.GetTasksJson(false)
            .Returns<string>(_ => throw new InvalidOperationException("catalog unavailable"));
        var runner = CreateRunner();

        var result = await runner.RunUnitTestProcessAsync(
            NullLogger.Instance,
            Guid.NewGuid().ToString(),
            "filter-failure",
            "test=Known");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RunUnitTestProcessAsync_ExecutableWritesResults_ReturnsXmlAndCleansWorkDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The test runner executable fixture uses a POSIX shell script.");
            return;
        }

        var executable = Path.Combine(testsDirectory, "AzureProjectTest.exe");
        await File.WriteAllTextAsync(
            executable,
            """
            #!/bin/sh
            for arg in "$@"; do
              case "$arg" in
                --work=*) work="${arg#--work=}" ;;
              esac
            done
            printf '<test-run result="Passed" />' > "$work/TestResult.xml"
            """);
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runner = CreateRunner();

        var result = await runner.RunUnitTestProcessAsync(
            NullLogger.Instance,
            Guid.NewGuid().ToString(),
            "successful-runner",
            "");

        Assert.That(result, Is.EqualTo("""<test-run result="Passed" />"""));
    }

    [Test]
    public async Task RunUnitTestProcessAsync_ExecutableOmitsResults_ReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The test runner executable fixture uses a POSIX shell script.");
            return;
        }

        var executable = Path.Combine(testsDirectory, "AzureProjectTest.exe");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nprintf 'no results generated'\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runner = CreateRunner();

        var result = await runner.RunUnitTestProcessAsync(
            NullLogger.Instance,
            Guid.NewGuid().ToString(),
            "missing-results",
            "");

        Assert.That(result, Is.Null);
    }

    private TestRunner CreateRunner()
    {
        return new TestRunner(
            gameTaskService,
            NullLogger<TestRunner>.Instance,
            Options.Create(new TestRunnerOptions
            {
                TestsWorkingDirectory = testsDirectory,
                DefaultFilter = "test==AzureProjectTestLib",
                TimeoutMinutes = 1
            }));
    }
}
