using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class GameTaskServiceTests
{
    private IStorageService storageService = null!;
    private IGameStateService gameStateService = null!;
    private GameTaskService service = null!;

    [SetUp]
    public void SetUp()
    {
        storageService = Substitute.For<IStorageService>();
        gameStateService = Substitute.For<IGameStateService>();
        service = new GameTaskService(
            NullLogger<GameTaskService>.Instance,
            storageService,
            gameStateService);
    }

    [Test]
    public void GetTasks_CoversEveryAttributedTestExactlyOnce()
    {
        var tasks = service.GetTasks(rephrases: false);
        var tests = tasks.SelectMany(task => task.Tests).ToList();
        var uniqueTests = tests.Distinct().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tasks, Has.Count.EqualTo(28));
            Assert.That(tests, Has.Count.EqualTo(35));
            Assert.That(uniqueTests, Has.Count.EqualTo(35));
            Assert.That(tasks, Is.Ordered.By("GameClassOrder"));
            Assert.That(tasks, Has.All.Property(nameof(GameTaskData.Instruction)).Not.Empty);
            Assert.That(tasks, Has.All.Property(nameof(GameTaskData.Filter)).Not.Empty);
        }
    }

    [Test]
    public void GetTasks_GroupsResourceGroupAssertionsIntoOneAssignment()
    {
        var task = service.GetTasks(false).Single(candidate =>
            candidate.Tests.Contains("AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.Tests, Is.EqualTo(new[]
            {
                "AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist",
                "AzureProjectTestLib.ResourceGroupTest.Test02_ResourceGroupLocation"
            }));
            Assert.That(task.Filter, Is.EqualTo(
                "test==\"AzureProjectTestLib.ResourceGroupTest.Test01_ResourceGroupExist\"||" +
                "test==\"AzureProjectTestLib.ResourceGroupTest.Test02_ResourceGroupLocation\""));
            Assert.That(task.Instruction, Does.Contain("'projProd'"));
            Assert.That(task.Instruction, Does.Contain("Azure East Asia"));
            Assert.That(task.Reward, Is.EqualTo(10));
            Assert.That(task.TimeLimit, Is.EqualTo(2));
        }
    }

    [Test]
    public void GetTasksJson_UsesCamelCaseProperties()
    {
        var tasks = JArray.Parse(service.GetTasksJson(false));
        var firstTask = (JObject)tasks[0]!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstTask.ContainsKey("gameClassOrder"), Is.True);
            Assert.That(firstTask.ContainsKey("instruction"), Is.True);
            Assert.That(firstTask.ContainsKey("GameClassOrder"), Is.False);
        }
    }

    [Test]
    public async Task GetNextTaskAsync_ReturnsFirstIncompleteTask()
    {
        var tasks = service.GetTasks(false);
        storageService.GetLastTaskNPCAsync("student@example.com").Returns((string?)null);
        storageService.GetCompletedTaskNamesAsync("student@example.com")
            .Returns(new List<string> { tasks[0].Name });

        var result = await service.GetNextTaskAsync("student@example.com", "Stella", "azure-learning");

        Assert.That(result?.Name, Is.EqualTo(tasks[1].Name));
    }

    [Test]
    public async Task GetNextTaskAsync_RecentTaskFromSameNpc_EnforcesCooldown()
    {
        storageService.GetLastTaskNPCAsync("student@example.com").Returns("Stella");
        gameStateService.GetGameStateAsync("student@example.com", "azure-learning", "Stella")
            .Returns(new GameState { LastUpdated = DateTime.UtcNow });

        var result = await service.GetNextTaskAsync("student@example.com", "Stella", "azure-learning");

        Assert.That(result, Is.Null);
        await storageService.DidNotReceive().GetCompletedTaskNamesAsync(Arg.Any<string>());
    }

    [Test]
    public async Task GetNextTaskAsync_AllTasksCompleted_ReturnsNull()
    {
        storageService.GetLastTaskNPCAsync("student@example.com").Returns((string?)null);
        storageService.GetCompletedTaskNamesAsync("student@example.com")
            .Returns(service.GetTasks(false).Select(task => task.Name).ToList());

        var result = await service.GetNextTaskAsync("student@example.com", "Stella", "azure-learning");

        Assert.That(result, Is.Null);
    }
}
