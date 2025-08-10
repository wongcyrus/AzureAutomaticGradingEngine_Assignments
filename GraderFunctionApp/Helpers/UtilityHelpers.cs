using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Helpers
{
    public static class UtilityHelpers
    {
        public static string ExtractEmail(string content)
        {
            const string matchEmailPattern =
                @"(([\w-]+\.)+[\w-]+|([a-zA-Z]{1}|[\w-]{2,}))@"
                + @"((([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\."
                + @"([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])){1}|"
                + @"([a-zA-Z]+[\w-]+\.)+[a-zA-Z]{2,4})";

            var rx = new Regex(
                matchEmailPattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Find matches.
            var matches = rx.Matches(content);
            if (matches.Count > 0)
            {
                return matches[0].Value;
            }
            return "Anonymous";
        }

        public static string GetTemporaryDirectory(string trace)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), Math.Abs(trace.GetHashCode()).ToString());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        public static string GetTestsWorkingDirectory()
        {
            // Cross-platform: prefer HOME or UserProfile, then combine path segments
            var overrideDir = Environment.GetEnvironmentVariable("TESTS_WORK_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return overrideDir!;
            }
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            var dir = Path.Combine(home!, "data", "Functions", "Tests");
            return dir;
        }

        public static string? ResolveDotnetExecutable(ILogger log)
        {
            try
            {
                // If DOTNET_ROOT is set, prefer it
                var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                if (!string.IsNullOrWhiteSpace(dotnetRoot))
                {
                    var candidate = Path.Combine(dotnetRoot!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                    if (File.Exists(candidate))
                    {
                        log.LogInformation("Using dotnet from DOTNET_ROOT: {path}", candidate);
                        return candidate;
                    }
                }

                if (OperatingSystem.IsWindows())
                {
                    var candidates = new[]
                    {
                        @"D:\Program Files\dotnet\dotnet.exe",
                        @"C:\Program Files\dotnet\dotnet.exe"
                    };
                    foreach (var c in candidates)
                    {
                        if (File.Exists(c))
                        {
                            log.LogInformation("Using dotnet at: {path}", c);
                            return c;
                        }
                    }
                }
                else
                {
                    var candidates = new[] { 
                        "/usr/bin/dotnet", 
                        "/usr/local/bin/dotnet", 
                        "/opt/dotnet/dotnet" 
                    };
                    foreach (var c in candidates)
                    {
                        if (File.Exists(c))
                        {
                            log.LogInformation("Using dotnet at: {path}", c);
                            return c;
                        }
                    }
                }

                // Fall back to just "dotnet" and hope it's on PATH
                log.LogInformation("Falling back to invoking 'dotnet' from PATH.");
                return "dotnet";
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to resolve dotnet executable.");
                return null;
            }
        }
    }
}
