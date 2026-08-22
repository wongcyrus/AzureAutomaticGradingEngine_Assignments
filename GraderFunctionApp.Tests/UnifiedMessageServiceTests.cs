using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using GraderFunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class UnifiedMessageServiceTests
{
    private IAIService aiService = null!;
    private IStorageService storageService = null!;
    private UnifiedMessageService service = null!;

    [SetUp]
    public void SetUp()
    {
        aiService = Substitute.For<IAIService>();
        storageService = Substitute.For<IStorageService>();
        service = new UnifiedMessageService(
            aiService,
            storageService,
            NullLogger<UnifiedMessageService>.Instance);
    }

    [Test]
    public async Task GetTaskAssignedMessageAsync_WithoutNpcData_UsesFallbackAndParameters()
    {
        var result = await service.GetTaskAssignedMessageAsync("Stella", "Task A", "Create resource A");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("Tek, "));
            Assert.That(result, Does.Contain("Task A"));
            Assert.That(result, Does.Contain("Create resource A"));
        }
        await aiService.DidNotReceive().PersonalizeNPCMessageAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task GetTaskCompletedMessageAsync_WithNpcData_ReturnsAiResult()
    {
        storageService.GetNPCCharacterAsync("Stella").Returns(CreateNpc());
        aiService.PersonalizeNPCMessageAsync(
                Arg.Is<string>(message => message.Contains("Task A") && message.Contains("10")),
                20,
                "female",
                "engineer")
            .Returns("Personalized completion");

        var result = await service.GetTaskCompletedMessageAsync("Stella", "Task A", 10);

        Assert.That(result, Is.EqualTo("Personalized completion"));
    }

    [Test]
    public async Task GetTaskFailedMessageAsync_SkipsAiAndReplacesProgress()
    {
        storageService.GetNPCCharacterAsync("Stella").Returns(CreateNpc());

        var result = await service.GetTaskFailedMessageAsync("Stella", "Task A", 2, 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("Tek, "));
            Assert.That(result, Does.Contain("Task A"));
            Assert.That(result, Does.Contain("2/3"));
        }
        await aiService.DidNotReceive().PersonalizeNPCMessageAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task GetBusyWithOtherNPCMessageAsync_ReplacesNpcName()
    {
        var result = await service.GetBusyWithOtherNPCMessageAsync("Stella", "Nova");

        Assert.That(result, Does.Contain("Nova"));
    }

    [Test]
    public async Task GetCooldownMessageAsync_ReplacesMinutes()
    {
        var result = await service.GetCooldownMessageAsync("Stella", 42);

        Assert.That(result, Does.Contain("42"));
    }

    [Test]
    public async Task GetPersonalizedMessageAsync_UnknownStatus_ReturnsDefaultMessage()
    {
        var result = await service.GetPersonalizedMessageAsync("UNKNOWN", "Stella");

        Assert.That(result, Is.EqualTo("Tek, Hello! How can I help you today?"));
    }

    [Test]
    public async Task GetPersonalizedMessageAsync_StorageFailure_ReturnsFallback()
    {
        storageService.GetNPCCharacterAsync("main_character")
            .Returns<Task<NPCCharacter?>>(_ => throw new InvalidOperationException("storage unavailable"));

        var result = await service.GetAllTasksCompletedMessageAsync("Stella");

        Assert.That(result, Does.StartWith("Tek, "));
    }

    [Test]
    public async Task GetTaskCompletedMessageAsync_EmptyAiResult_UsesFallback()
    {
        storageService.GetNPCCharacterAsync("Stella").Returns(CreateNpc());
        aiService.PersonalizeNPCMessageAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns((string?)null);

        var result = await service.GetTaskCompletedMessageAsync("Stella", "Task A", 10);

        Assert.That(result, Does.StartWith("Tek, "));
    }

    private static NPCCharacter CreateNpc()
    {
        return new NPCCharacter
        {
            RowKey = "Stella",
            Name = "Stella",
            Age = 20,
            Gender = "female",
            Background = "engineer"
        };
    }
}
