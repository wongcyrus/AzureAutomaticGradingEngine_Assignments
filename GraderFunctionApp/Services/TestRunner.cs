using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Models;

namespace GraderFunctionApp.Services
{
    public static class TestRunner
    {
        public static async Task<string?> RunUnitTestProcess(ExecutionContext context, ILogger log, string credentials, string trace, string filter)
        {
            var tempDir = UtilityHelpers.GetTemporaryDirectory(trace);
            var tempCredentialsFilePath = Path.Combine(tempDir, "azureauth.json");

            await File.WriteAllTextAsync(tempCredentialsFilePath, credentials);

            var workingDirectoryInfo = UtilityHelpers.GetTestsWorkingDirectory();
            var exeLocation = Path.Combine(workingDirectoryInfo, "AzureProjectTest.exe");
            var dllLocation = Path.Combine(workingDirectoryInfo, "AzureProjectTest.dll");
            log.LogInformation("Unit Test Runner Location: exe={exeLocation} dll={dllLocation}", exeLocation, dllLocation);

            if (string.IsNullOrWhiteSpace(filter))
            {
                filter = "test==AzureProjectTestLib";
            }
            else
            {
                try
                {
                    var serializerSettings = new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    };
                    var jsonText = GameTaskFunction.GetTasksJson(false);
                    var json = JsonConvert.DeserializeObject<List<GameTaskData>>(jsonText, serializerSettings);
                    var mapped = json?.FirstOrDefault(c => string.Equals(c.Filter, filter, StringComparison.OrdinalIgnoreCase))?.Filter;
                    if (!string.IsNullOrWhiteSpace(mapped))
                    {
                        filter = mapped!;
                    }
                    else
                    {
                        log.LogWarning("Filter name '{filter}' not found in tasks mapping. Using provided filter as-is.", filter);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to map filter name. Using provided filter as-is.");
                }
            }

            log.LogInformation($@"{tempCredentialsFilePath} {tempDir} {trace} {filter}");
            try
            {
                // Local function to execute the runner once
                async Task<(bool ok, string? xml)> RunOnceAsync(string fileName, IEnumerable<string> initialArgs)
                {
                    using var process = new Process();
                    var info = new ProcessStartInfo
                    {
                        // Don't set WorkingDirectory to app directory for framework-dependent apps
                        // WorkingDirectory = workingDirectoryInfo,
                        FileName = fileName,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };

                    foreach (var a in initialArgs)
                    {
                        info.ArgumentList.Add(a);
                    }
                    // Common named flags
                    info.ArgumentList.Add($"--credentials={tempCredentialsFilePath}");
                    info.ArgumentList.Add($"--work={tempDir}");
                    info.ArgumentList.Add($"--trace={trace}");
                    info.ArgumentList.Add($"--where={filter}");

                    process.StartInfo = info;

                    log.LogInformation("Refresh start.");
                    process.Refresh();
                    log.LogInformation("Process start. Command: {cmd} {args}", fileName, string.Join(' ', info.ArgumentList));
                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    using AutoResetEvent outputWaitHandle = new(false);
                    using AutoResetEvent errorWaitHandle = new(false);
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            outputWaitHandle.Set();
                        }
                        else
                        {
                            output.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            errorWaitHandle.Set();
                        }
                        else
                        {
                            error.AppendLine(e.Data);
                        }
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    const int timeout = 5 * 60 * 1000;
                    if (process.WaitForExit(timeout) &&
                        outputWaitHandle.WaitOne(timeout) &&
                        errorWaitHandle.WaitOne(timeout))
                    {
                        log.LogInformation("Process Ended. ExitCode={code}", process.ExitCode);
                        var errorLog = error.ToString();
                        if (!string.IsNullOrWhiteSpace(errorLog))
                        {
                            log.LogWarning("Test runner stderr: {stderr}", errorLog);
                        }

                        var resultPath = Path.Combine(tempDir, "TestResult.xml");
                        if (File.Exists(resultPath))
                        {
                            var xml = await File.ReadAllTextAsync(resultPath);
                            return (true, xml);
                        }
                        else
                        {
                            log.LogError("TestResult.xml not found in work directory: {dir}", tempDir);
                            return (false, null);
                        }
                    }
                    else
                    {
                        log.LogInformation("Process Timed out.");
                        return (false, null);
                    }
                }

                // Prefer EXE if available (self-contained), otherwise try dotnet + DLL
                if (File.Exists(exeLocation))
                {
                    var (ok, xml) = await RunOnceAsync(exeLocation, Array.Empty<string>());
                    if (ok && !string.IsNullOrWhiteSpace(xml))
                    {
                        return xml;
                    }
                    log.LogWarning("EXE run did not produce results. Will attempt DLL with dotnet if available.");
                }
                if (File.Exists(dllLocation))
                {
                    var dotnetPath = UtilityHelpers.ResolveDotnetExecutable(log);
                    if (!string.IsNullOrWhiteSpace(dotnetPath))
                    {
                        var (ok, xml) = await RunOnceAsync(dotnetPath!, new[] { dllLocation });
                        if (ok && !string.IsNullOrWhiteSpace(xml))
                        {
                            return xml;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    log.LogWarning(cleanupEx, "Failed to cleanup temp directory.");
                }
            }
            return null;
        }
    }
}
