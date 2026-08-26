using System.Text;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class MessageGeneratorFunctionTests
{
    private IPreGeneratedMessageService preGeneratedService = null!;
    private IUnifiedMessageService messageService = null!;
    private IOperatorRequestAuthorizer operatorRequestAuthorizer = null!;
    private MessageGeneratorFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        preGeneratedService = Substitute.For<IPreGeneratedMessageService>();
        messageService = Substitute.For<IUnifiedMessageService>();
        operatorRequestAuthorizer =
            Substitute.For<IOperatorRequestAuthorizer>();
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Authorized);
        function = new MessageGeneratorFunction(
            NullLogger<MessageGeneratorFunction>.Instance,
            preGeneratedService,
            messageService,
            operatorRequestAuthorizer);
    }

    [Test]
    public async Task RefreshAllMessagesAsync_MissingSignedIdentity_ReturnsUnauthorized()
    {
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Unauthenticated);

        var result = await function.RefreshAllMessagesAsync(CreateRequest());

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task AllEndpoints_SignedStudent_ReturnForbiddenWithoutServiceCalls()
    {
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Forbidden);

        IActionResult[] results =
        [
            await function.RefreshAllMessagesAsync(CreateRequest()),
            await function.GeneratePersonalizedMessageAsync(CreateRequest()),
            await function.TestMessageGenerationAsync(CreateRequest())
        ];

        Assert.That(results, Has.All.TypeOf<ForbidResult>());
        Assert.That(preGeneratedService.ReceivedCalls(), Is.Empty);
        Assert.That(messageService.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task RefreshAllMessagesAsync_Success_ReturnsStatistics()
    {
        preGeneratedService.GetHitCountStatsAsync().Returns(new PreGeneratedMessageStats
        {
            TotalMessages = 10,
            InstructionMessages = 4,
            NPCMessages = 6
        });

        var result = await function.RefreshAllMessagesAsync(CreateRequest());

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await preGeneratedService.Received(1).RefreshAllPreGeneratedMessagesAsync();
    }

    [Test]
    public async Task RefreshAllMessagesAsync_Failure_ReturnsInternalServerError()
    {
        preGeneratedService.RefreshAllPreGeneratedMessagesAsync()
            .Returns<Task>(_ => throw new InvalidOperationException("refresh failed"));

        var result = await function.RefreshAllMessagesAsync(CreateRequest());

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(500));
    }

    [TestCase("")]
    [TestCase("{")]
    [TestCase("""{"status":"TASK_ASSIGNED"}""")]
    public async Task GeneratePersonalizedMessageAsync_InvalidBody_ReturnsBadRequest(string body)
    {
        var result = await function.GeneratePersonalizedMessageAsync(CreateRequest(body));

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GeneratePersonalizedMessageAsync_ValidBody_ReturnsMessage()
    {
        messageService.GetPersonalizedMessageAsync(
                "TASK_ASSIGNED",
                "Stella",
                Arg.Any<Dictionary<string, object>>())
            .Returns("Personalized");

        var result = await function.GeneratePersonalizedMessageAsync(CreateRequest(
            """{"status":"TASK_ASSIGNED","npcName":"Stella","parameters":{"TaskName":"Task A"}}"""));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await messageService.Received(1).GetPersonalizedMessageAsync(
            "TASK_ASSIGNED",
            "Stella",
            Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task GeneratePersonalizedMessageAsync_ServiceFailure_ReturnsInternalServerError()
    {
        messageService.GetPersonalizedMessageAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("generation failed"));

        var result = await function.GeneratePersonalizedMessageAsync(CreateRequest(
            """{"status":"TASK_ASSIGNED","npcName":"Stella","parameters":{}}"""));

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task TestMessageGenerationAsync_GeneratesEveryMessageType()
    {
        messageService.GetTaskAssignedMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("assigned");
        messageService.GetTaskCompletedMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns("completed");
        messageService.GetTaskFailedMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns("failed");
        messageService.GetBusyWithOtherNPCMessageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns("busy");
        messageService.GetCooldownMessageAsync(Arg.Any<string>(), Arg.Any<int>()).Returns("cooldown");
        messageService.GetActiveTaskReminderMessageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns("reminder");
        messageService.GetAllTasksCompletedMessageAsync(Arg.Any<string>()).Returns("all completed");

        var result = await function.TestMessageGenerationAsync(CreateRequest());

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await messageService.Received(1).GetAllTasksCompletedMessageAsync("TestNPC");
    }

    [Test]
    public async Task TestMessageGenerationAsync_ServiceFailure_ReturnsInternalServerError()
    {
        messageService.GetTaskAssignedMessageAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("generation failed"));

        var result = await function.TestMessageGenerationAsync(CreateRequest());

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(500));
    }

    private static HttpRequest CreateRequest(string body = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }
}
