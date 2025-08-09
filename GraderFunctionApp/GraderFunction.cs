using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GraderFunctionApp
{
    public class GraderFunction
    {

        public class RequestBodyModel
        {
            public required string Trace { get; set; }
            public required string Credentials { get; set; }
            public required string Filter { get; set; }
        }

        private readonly ILogger _logger;
        public GraderFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GraderFunction>();
        }

        [Function(nameof(AzureGraderFunction))]
        public async Task<IActionResult> AzureGraderFunction(
             [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
             ExecutionContext context)
        {
            _logger.LogInformation("Start AzureGraderFunction");


            if (req.Method == "GET")
            {
                if (!req.Query.ContainsKey("credentials"))
                {
                    const string html = @"
<!DOCTYPE html>
<html lang='en' xmlns='http://www.w3.org/1999/xhtml'>
<head>
    <meta charset='utf-8' />
    <title>Azure Grader</title>
</head>
<body>
    <form id='contact-form' method='post'>
        Azure Credentials<br/>
        <textarea name='credentials' required  rows='15' cols='100'></textarea>
        <br/>
        NUnit Test Name<br/>
        <input type='text' id='filter' name='filter' size='50'/><br/>
        <button type='submit'>Run Test</button>
    </form>
    <footer>
        <p>Developed by <a href='https://www.vtc.edu.hk/admission/en/programme/it114115-higher-diploma-in-cloud-and-data-centre-administration/'> Higher Diploma in Cloud and Data Centre Administration Team.</a></p>
    </footer>
</body>
</html>";


                    return new ContentResult()
                    {
                        Content = html,
                        ContentType = "text/html",
                        StatusCode = 200,
                    };
                }

                string credentials = req.Query["credentials"]!;
                string filter = req.Query["filter"]!;

                string? xml;
                if (req.Query.ContainsKey("trace"))
                {
                    string trace = req.Query["trace"]!;
                    var email = ExtractEmail(trace);
                    _logger.LogInformation("start:" + trace);
                    xml = await RunUnitTestProcess(context, _logger, credentials, email, filter);
                    _logger.LogInformation("end:" + trace);
                }
                else
                {
                    xml = await RunUnitTestProcess(context, _logger, credentials, "Anonymous", filter);
                }
                if (string.IsNullOrEmpty(xml))
                {
                    return new ContentResult { Content = "<error>Failed to run tests or no results produced.</error>", ContentType = "application/xml", StatusCode = 500 };
                }
                return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };

            }

            if (req.Method == "POST")
            {
                _logger.LogInformation("POST Request");
                string needXml = req.Query["xml"]!;
                string credentials = req.Form["credentials"]!;
                string filter = req.Form["filter"]!;
                if (string.IsNullOrWhiteSpace(credentials))
                {
                    return new ContentResult
                    {
                        Content = $"<result><value>No credentials</value></result>",
                        ContentType = "application/xml",
                        StatusCode = 422
                    };
                }
                var xml = await RunUnitTestProcess(context, _logger, credentials, "Anonymous", filter);
                if (string.IsNullOrEmpty(xml))
                {
                    return new ContentResult { Content = "<error>Failed to run tests or no results produced.</error>", ContentType = "application/xml", StatusCode = 500 };
                }
                if (string.IsNullOrEmpty(needXml))
                {
                    var result = ParseNUnitTestResult(xml!);
                    return new JsonResult(result);
                }
                return new ContentResult { Content = xml, ContentType = "application/xml", StatusCode = 200 };
            }

            return new OkObjectResult("ok");
        }

        private static async Task<string?> RunUnitTestProcess(ExecutionContext context, ILogger log, string credentials, string trace, string filter)
        {
            var tempDir = GetTemporaryDirectory(trace);
            var tempCredentialsFilePath = Path.Combine(tempDir, "azureauth.json");

            await File.WriteAllTextAsync(tempCredentialsFilePath, credentials);

            var workingDirectoryInfo = GetTestsWorkingDirectory();
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
                    var mapped = json?.FirstOrDefault(c => string.Equals(c.Name, filter, StringComparison.OrdinalIgnoreCase))?.Filter;
                    if (!string.IsNullOrWhiteSpace(mapped))
                    {
                        filter = mapped!;
                    }
                    else
                    {
                        log.LogWarning("Filter name '{filter}' not found in tasks mapping. Using provided filter as-is.", filter);
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
                using var process = new Process();
                var useDotnet = !File.Exists(exeLocation) && File.Exists(dllLocation);
                var info = new ProcessStartInfo
                {
                    WorkingDirectory = workingDirectoryInfo,
                    FileName = useDotnet ? "dotnet" : exeLocation,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                // On non-Windows, prefer running the DLL via dotnet if the EXE is unavailable
                if (useDotnet)
                {
                    info.ArgumentList.Add(dllLocation);
                }

                // Use named flags supported by the test runner to avoid positional/quoting issues
                // --credentials, --work, --trace, --where
                info.ArgumentList.Add($"--credentials={tempCredentialsFilePath}");
                info.ArgumentList.Add($"--work={tempDir}");
                info.ArgumentList.Add($"--trace={trace}");
                info.ArgumentList.Add($"--where={filter}");

                process.StartInfo = info;

                log.LogInformation("Refresh start.");
                process.Refresh();
                log.LogInformation("Process start.");
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
                    // Process completed. Check process.ExitCode here.
                    log.LogInformation("Process Ended.");
                    //log.LogInformation(output.ToString());

                    var errorLog = error.ToString();
                    if (!string.IsNullOrWhiteSpace(errorLog))
                    {
                        // Some runners write warnings to stderr; log but don't fail solely on stderr content
                        log.LogWarning("Test runner stderr: {stderr}", errorLog);
                    }

                    var resultPath = Path.Combine(tempDir, "TestResult.xml");
                    if (File.Exists(resultPath))
                    {
                        var xml = await File.ReadAllTextAsync(resultPath);
                        return xml;
                    }
                    else
                    {
                        log.LogError("TestResult.xml not found in work directory: {dir}", tempDir);
                        return null;
                    }
                }
                else
                {
                    // Timed out.
                    log.LogInformation("Process Timed out.");
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

        private static string GetTemporaryDirectory(string trace)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), Math.Abs(trace.GetHashCode()).ToString());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }
        private static string GetTestsWorkingDirectory()
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
