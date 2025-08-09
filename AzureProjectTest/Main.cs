using System.Reflection;
using AzureProjectTestLib.Helper;
using NUnit.Common;
using NUnitLite;

namespace AzureProjectTest;

internal class Run
{
    private static int Main(string[] args)
    {

        // var trace = "trace";
        // var tempDir = @"/workspaces/AzureAutomaticGradingEngine_Assignments/testing";
        // var tempCredentialsFilePath = @"/workspaces/AzureAutomaticGradingEngine_Assignments/testing/sp.json";
        // var where = "";
        // var where = "test==\"AzureProjectTestLib.VnetTests.Test01_Have2VnetsIn2Regions\"||test==\"AzureProjectTestLib.VnetTests.Test02_VnetAddressSpace\"";
        // var where = "test==\"AzureProjectTestLib.AppServiceTest.Test04_FunctionAppSettings\"";

        // Defaults relative to the repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var defaultWorkDir = Path.Combine(repoRoot, "testing");
        var defaultCredsPath = Path.Combine(defaultWorkDir, "sp.json");

        string tempCredentialsFilePath = defaultCredsPath;
        string tempDir = defaultWorkDir;
        string trace = "trace";
        string where = string.Empty;

        // Support named flags and positional args
        int pos = 0;
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) { pos++; continue; }
            if (arg.StartsWith("--"))
            {
                var parts = arg.Split(new[] { '=' }, 2);
                var key = parts[0].Trim();
                var val = parts.Length > 1 ? parts[1] : string.Empty;
                switch (key.ToLowerInvariant())
                {
                    case "--creds":
                    case "--cred":
                    case "--credentials":
                        if (!string.IsNullOrWhiteSpace(val)) tempCredentialsFilePath = val;
                        break;
                    case "--work":
                    case "--out":
                    case "--dir":
                        if (!string.IsNullOrWhiteSpace(val)) tempDir = val;
                        break;
                    case "--trace":
                        if (!string.IsNullOrWhiteSpace(val)) trace = val;
                        break;
                    case "--where":
                        if (!string.IsNullOrEmpty(val)) where = val;
                        break;
                }
            }
            else
            {
                switch (pos)
                {
                    case 0: tempCredentialsFilePath = arg; break;
                    case 1: tempDir = arg; break;
                    case 2: trace = arg; break;
                    case 3: where = arg; break;
                }
                pos++;
            }
        }
        Console.WriteLine($"AzureProjectTest starting with:\n  creds: {tempCredentialsFilePath}\n  work:  {tempDir}\n  trace: {trace}\n  where: {where}");


        var strWriter = new StringWriter();
        var autoRun = new AutoRun(typeof(Config).GetTypeInfo().Assembly);

        var runTestParameters = new List<string>
        {
            "/test:AzureProjectTestLib",
            "--work=" + tempDir,
            "--output=" + tempDir,
            "--err=" + tempDir,
            "--params:AzureCredentialsPath=" + tempCredentialsFilePath + ";trace=" + trace
        };
        if (!string.IsNullOrWhiteSpace(where))
        {
            runTestParameters.Insert(1, "--where=" + where);
        }
        Console.WriteLine("NUnit args: " + string.Join(" ", runTestParameters));
        var returnCode = autoRun.Execute(runTestParameters.ToArray(), new ExtendedTextWrapper(strWriter), Console.In);

        Console.WriteLine(strWriter.ToString());

        //var xml = File.ReadAllText(Path.Combine(tempDir, "TestResult.xml"));
        //Console.WriteLine(returnCode);
        //Console.WriteLine(xml);
        return returnCode;
    }
}