using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using GraderFunctionApp.Helpers;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using GraderFunctionApp.Configuration;

namespace GraderFunctionApp.Services
{
    public class TestRunner : ITestRunner
    {
        private readonly IGameTaskService _gameTaskService;
        private readonly ILogger<TestRunner> _logger;
        private readonly TestRunnerOptions _options;

        public TestRunner(IGameTaskService gameTaskService, ILogger<TestRunner> logger, IOptions<TestRunnerOptions> options)
        {
            _gameTaskService = gameTaskService;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<string?> RunUnitTestProcessAsync(ILogger log, string credentials, string trace, string filter)
        {
            var tempDir = UtilityHelpers.GetTemporaryDirectory(trace);
            var tempCredentialsFilePath = Path.Combine(tempDir, "azureauth.json");

            await File.WriteAllTextAsync(tempCredentialsFilePath, credentials);

            var workingDirectoryInfo = GetTestsWorkingDirectory();
            var exeLocation = Path.Combine(workingDirectoryInfo, "AzureProjectTest.exe");
            var dllLocation = Path.Combine(workingDirectoryInfo, "AzureProjectTest.dll");
            
            log.LogInformation("Unit Test Runner Location: exe={exeLocation} dll={dllLocation}", exeLocation, dllLocation);

            filter = await ProcessFilterAsync(filter, log);
            if (filter == null)
            {
                return null;
            }

            log.LogInformation($@"{tempCredentialsFilePath} {tempDir} {trace} {filter}");
            
            try
            {
                // Prefer EXE if available (self-contained), otherwise try dotnet + DLL
                if (File.Exists(exeLocation))
                {
                    var (ok, xml) = await RunTestExecutableAsync(exeLocation, Array.Empty<string>(), tempCredentialsFilePath, tempDir, trace, filter, log);
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
                        var (ok, xml) = await RunTestExecutableAsync(dotnetPath!, new[] { dllLocation }, tempCredentialsFilePath, tempDir, trace, filter, log);
                        if (ok && !string.IsNullOrWhiteSpace(xml))
                        {
                            return xml;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error occurred while running unit tests");
            }
            finally
            {
                await CleanupTempDirectoryAsync(tempDir, log);
            }
            
            return null;
        }

        private Task<string> ProcessFilterAsync(string filter, ILogger log)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return Task.FromResult(_options.DefaultFilter);
            }

            try
            {
                var serializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                
                var jsonText = _gameTaskService.GetTasksJson(false);
                var json = JsonConvert.DeserializeObject<List<GameTaskData>>(jsonText, serializerSettings);
                var mapped = json?.FirstOrDefault(c => string.Equals(c.Filter, filter, StringComparison.OrdinalIgnoreCase))?.Filter;
                
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    return Task.FromResult(mapped!);
                }
                else
                {
                    log.LogWarning("Filter name '{filter}' not found in tasks mapping. Using provided filter as-is.", filter);
                    return Task.FromResult<string>(null!);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to map filter name. Using provided filter as-is.");
                return Task.FromResult(filter);
            }
        }

        private async Task<(bool ok, string? xml)> RunTestExecutableAsync(
            string fileName, 
            IEnumerable<string> initialArgs, 
            string credentialsPath, 
            string workDir, 
            string trace, 
            string filter, 
            ILogger log)
        {
            using var process = new Process();
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var arg in initialArgs)
            {
                info.ArgumentList.Add(arg);
            }
            
            // Common named flags
            info.ArgumentList.Add($"--credentials={credentialsPath}");
            info.ArgumentList.Add($"--work={workDir}");
            info.ArgumentList.Add($"--trace={trace}");
            info.ArgumentList.Add($"--where={filter}");

            process.StartInfo = info;

            log.LogInformation("Process start. Command: {cmd} {args}", fileName, string.Join(' ', info.ArgumentList));
            
            var output = new StringBuilder();
            var error = new StringBuilder();
            
            using var outputWaitHandle = new AutoResetEvent(false);
            using var errorWaitHandle = new AutoResetEvent(false);
            
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

            var timeout = _options.TimeoutMinutes * 60 * 1000;
            
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

                var resultPath = Path.Combine(workDir, "TestResult.xml");
                if (File.Exists(resultPath))
                {
                    var xml = await File.ReadAllTextAsync(resultPath);
                    return (true, xml);
                }
                else
                {
                    log.LogError("TestResult.xml not found in work directory: {dir}", workDir);
                    return (false, null);
                }
            }
            else
            {
                log.LogWarning("Process timed out after {timeout} minutes.", _options.TimeoutMinutes);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to kill timed out process");
                }
                return (false, null);
            }
        }

        private string GetTestsWorkingDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_options.TestsWorkingDirectory))
            {
                return _options.TestsWorkingDirectory;
            }
            
            return UtilityHelpers.GetTestsWorkingDirectory();
        }

        private async Task CleanupTempDirectoryAsync(string tempDir, ILogger log)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    await Task.Run(() => Directory.Delete(tempDir, true));
                }
            }
            catch (Exception cleanupEx)
            {
                log.LogWarning(cleanupEx, "Failed to cleanup temp directory: {tempDir}", tempDir);
            }
        }
    }
}
