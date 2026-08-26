using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class PreGeneratedMessageStatsFunctionTests
{
    private IPreGeneratedMessageService service = null!;
    private IOperatorRequestAuthorizer operatorRequestAuthorizer = null!;
    private PreGeneratedMessageStatsFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        service = Substitute.For<IPreGeneratedMessageService>();
        operatorRequestAuthorizer =
            Substitute.For<IOperatorRequestAuthorizer>();
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Authorized);
        function = new PreGeneratedMessageStatsFunction(
            NullLogger<PreGeneratedMessageStatsFunction>.Instance,
            service,
            operatorRequestAuthorizer);
    }

    [Test]
    public async Task Run_MissingSignedIdentity_ReturnsUnauthorized()
    {
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Unauthenticated);

        var result = await function.Run(CreateRequest());

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task AllEndpoints_SignedStudent_ReturnForbiddenWithoutServiceCalls()
    {
        operatorRequestAuthorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Forbidden);

        IActionResult[] results =
        [
            await function.Run(CreateRequest()),
            await function.ResetHitCounts(CreateRequest()),
            await function.TestCacheLookup(CreateRequest()),
            await function.ClearAllMessages(CreateRequest())
        ];

        Assert.That(results, Has.All.TypeOf<ForbidResult>());
        Assert.That(service.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task Run_ReturnsStatistics()
    {
        service.GetHitCountStatsAsync().Returns(new PreGeneratedMessageStats
        {
            TotalMessages = 4,
            TotalHits = 8,
            InstructionMessages = 2,
            InstructionHits = 3,
            NPCMessages = 2,
            NPCHits = 5,
            MostUsedMessage = CreateMessage(new string('x', 110), 7),
            LeastUsedMessage = CreateMessage("short", 1)
        });

        var result = await function.Run(CreateRequest());

        Assert.That(result, Is.TypeOf<OkObjectResult>());
    }

    [Test]
    public async Task Run_ServiceFailure_ReturnsInternalServerError()
    {
        service.GetHitCountStatsAsync()
            .Returns<Task<PreGeneratedMessageStats>>(_ => throw new InvalidOperationException());

        var result = await function.Run(CreateRequest());

        Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task ResetHitCounts_Success_ReturnsOk()
    {
        var result = await function.ResetHitCounts(CreateRequest());

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await service.Received(1).ResetHitCountsAsync();
    }

    [Test]
    public async Task ResetHitCounts_Failure_ReturnsInternalServerError()
    {
        service.ResetHitCountsAsync().Returns<Task>(_ => throw new InvalidOperationException());

        var result = await function.ResetHitCounts(CreateRequest());

        Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task TestCacheLookup_UsesProvidedParameters()
    {
        var request = CreateRequest();
        request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["message"] = "Hello",
            ["age"] = "30",
            ["gender"] = "Male",
            ["background"] = "Teacher"
        });
        service.GetPreGeneratedNPCMessageAsync("Hello", 30, "Male", "Teacher")
            .Returns("Cached");

        var result = await function.TestCacheLookup(request);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await service.Received(1).GetPreGeneratedNPCMessageAsync("Hello", 30, "Male", "Teacher");
    }

    [Test]
    public async Task TestCacheLookup_Failure_ReturnsInternalServerError()
    {
        service.GetPreGeneratedNPCMessageAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException());

        var result = await function.TestCacheLookup(CreateRequest());

        Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task ClearAllMessages_Success_ReturnsOk()
    {
        var result = await function.ClearAllMessages(CreateRequest());

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await service.Received(1).ClearAllPreGeneratedMessagesAsync();
    }

    [Test]
    public async Task ClearAllMessages_Failure_ReturnsInternalServerError()
    {
        service.ClearAllPreGeneratedMessagesAsync()
            .Returns<Task>(_ => throw new InvalidOperationException());

        var result = await function.ClearAllMessages(CreateRequest());

        Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(500));
    }

    private static HttpRequest CreateRequest()
    {
        return new DefaultHttpContext().Request;
    }

    private static PreGeneratedMessage CreateMessage(string original, int hits)
    {
        return new PreGeneratedMessage
        {
            PartitionKey = "npc",
            RowKey = Guid.NewGuid().ToString(),
            OriginalMessage = original,
            GeneratedMessage = "generated",
            MessageType = "npc",
            HitCount = hits
        };
    }
}
