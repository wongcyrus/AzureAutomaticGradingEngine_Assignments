using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class MessageRefreshTimerFunctionTests
{
    [Test]
    public async Task RunAsync_RefreshesMessages()
    {
        var service = Substitute.For<IPreGeneratedMessageService>();
        var function = new MessageRefreshTimerFunction(
            NullLogger<MessageRefreshTimerFunction>.Instance,
            service);

        await function.RunAsync(new TimerInfo());

        await service.Received(1).RefreshAllPreGeneratedMessagesAsync();
    }

    [Test]
    public void RunAsync_ServiceFailure_Rethrows()
    {
        var service = Substitute.For<IPreGeneratedMessageService>();
        service.RefreshAllPreGeneratedMessagesAsync()
            .Returns<Task>(_ => throw new InvalidOperationException("refresh failed"));
        var function = new MessageRefreshTimerFunction(
            NullLogger<MessageRefreshTimerFunction>.Instance,
            service);
        Func<Task> action = async () => await function.RunAsync(new TimerInfo());

        Assert.ThrowsAsync<InvalidOperationException>(action);
    }
}
