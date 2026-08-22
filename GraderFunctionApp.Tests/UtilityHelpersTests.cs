using GraderFunctionApp.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraderFunctionApp.Tests;

public class UtilityHelpersTests
{
    [TestCase("Contact Student.Name@example.com now", "Student.Name@example.com")]
    [TestCase("No email is present", "Anonymous")]
    [TestCase("", "Anonymous")]
    public void ExtractEmail_ReturnsFirstEmailOrAnonymous(string content, string expected)
    {
        Assert.That(UtilityHelpers.ExtractEmail(content), Is.EqualTo(expected));
    }

    [Test]
    public void GetTemporaryDirectory_CreatesDirectory()
    {
        var directory = UtilityHelpers.GetTemporaryDirectory("unit-test");

        try
        {
            Assert.That(Directory.Exists(directory), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public void GetTestsWorkingDirectory_UsesEnvironmentOverride()
    {
        var original = Environment.GetEnvironmentVariable("TESTS_WORK_DIR");

        try
        {
            Environment.SetEnvironmentVariable("TESTS_WORK_DIR", "/tmp/grader-tests");

            Assert.That(UtilityHelpers.GetTestsWorkingDirectory(), Is.EqualTo("/tmp/grader-tests"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTS_WORK_DIR", original);
        }
    }

    [Test]
    [NonParallelizable]
    public void GetTestsWorkingDirectory_UsesHomeWhenNoOverrideExists()
    {
        var originalOverride = Environment.GetEnvironmentVariable("TESTS_WORK_DIR");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("TESTS_WORK_DIR", null);
            Environment.SetEnvironmentVariable("HOME", "/tmp/grader-home");

            Assert.That(
                UtilityHelpers.GetTestsWorkingDirectory(),
                Is.EqualTo(Path.Combine("/tmp/grader-home", "data", "Functions", "Tests")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTS_WORK_DIR", originalOverride);
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }

    [Test]
    [NonParallelizable]
    public void ResolveDotnetExecutable_UsesExistingDotnetRootExecutable()
    {
        var originalRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        File.WriteAllText(executable, "");

        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);

            var result = UtilityHelpers.ResolveDotnetExecutable(NullLogger.Instance);

            Assert.That(result, Is.EqualTo(executable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public void ResolveDotnetExecutable_WithoutDotnetRoot_ReturnsSystemExecutableOrPathCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("This assertion covers the Unix dotnet search paths.");
            return;
        }

        var originalRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", null);

            var result = UtilityHelpers.ResolveDotnetExecutable(NullLogger.Instance);

            Assert.That(result, Is.AnyOf(
                "/usr/bin/dotnet",
                "/usr/local/bin/dotnet",
                "/opt/dotnet/dotnet",
                "dotnet"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalRoot);
        }
    }
}
