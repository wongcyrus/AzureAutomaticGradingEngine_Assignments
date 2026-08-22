using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DotNetEnv;

class Program
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static string? AzureFunctionUrl { get; private set; }
    static async Task Main()
    {
        Env.Load();
        AzureFunctionUrl = Environment.GetEnvironmentVariable("AZURE_FUNCTION_URL")
        ?? throw new InvalidOperationException("AZURE_FUNCTION_URL environment variable not set");

        string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID environment variable not set");
        if (!Guid.TryParse(subscriptionId, out _))
            throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID must be a valid GUID.");

        // Load tasks.json
        string tasksPath = Path.Combine(Directory.GetCurrentDirectory(), "tasks.json");
        if (!File.Exists(tasksPath))
        {
            Console.WriteLine("tasks.json not found.");
            return;
        }
        var tasksJson = await File.ReadAllTextAsync(tasksPath);
        var tasks = JsonSerializer.Deserialize<TaskItem[]>(tasksJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var report = new List<ReportRow>();

        // Ensure report directory exists
        string reportDir = Path.Combine(Directory.GetCurrentDirectory(), "report");
        Directory.CreateDirectory(reportDir);

        if (tasks == null)
        {
            Console.WriteLine("No tasks found in tasks.json");
            return;
        }
        foreach (var task in tasks)
        {
            Console.WriteLine($"\nRunning task: {task.Name}");
            var stopwatch = Stopwatch.StartNew();

            // Use filter from tasks.json if available, otherwise fallback to Name
            string filterValue = task.Filter ?? task.Name ?? string.Empty;

            // Debug: print the filter value being sent
            Console.WriteLine($"Using filter: '{filterValue}'");

            // Skip tasks with no filter value
            if (string.IsNullOrWhiteSpace(filterValue))
            {
                Console.WriteLine("Skipping task because filter is empty.");
                continue;
            }

            var formData = new Dictionary<string, string>
            {
                { "subscriptionId", subscriptionId },
                { "filter", filterValue }
            };
            var content = new FormUrlEncodedContent(formData);

            var response = await httpClient.PostAsync(AzureFunctionUrl, content);
            string result = await response.Content.ReadAsStringAsync();

            stopwatch.Stop();
            double seconds = stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"Task '{task.Name}' completed in {seconds:F2} seconds");
            Console.WriteLine($"Response length: {result.Length} chars");

            // Save individual response to file in report folder
            string safeTaskName = string.Join("_", (task.Name ?? "").Split(Path.GetInvalidFileNameChars()));
            string taskFileName = Path.Combine(reportDir, $"response_{safeTaskName}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(taskFileName, result);

            report.Add(new ReportRow
            {
                TaskName = task.Name,
                DurationSeconds = seconds,
                ResponseLength = result.Length
            });
        }

        // Save CSV report in report folder
        string reportPath = Path.Combine(reportDir, $"task_report_{DateTime.Now:yyyyMMdd_HHmms}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("TaskName,DurationSeconds,ResponseLength");
        foreach (var row in report)
        {
            sb.AppendLine($"{row.TaskName},{row.DurationSeconds:F2},{row.ResponseLength}");
        }
        await File.WriteAllTextAsync(reportPath, sb.ToString());

        Console.WriteLine($"\nReport saved to {reportPath}");
    }

    public class TaskItem
    {
        public string? Name { get; set; }
        public string? Filter { get; set; } // Added Filter property
    }

    public class ReportRow
    {
        public string? TaskName { get; set; }
        public double DurationSeconds { get; set; }
        public int ResponseLength { get; set; }
    }
}
