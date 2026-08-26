using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class StudentRegistrationAdminFunctionTests
{
    private readonly Dictionary<string, SubscriptionRegistration> registrations =
        new(StringComparer.Ordinal);
    private IOperatorRequestAuthorizer authorizer = null!;
    private StudentRegistrationAdminFunction function = null!;
    private TableClient table = null!;

    [SetUp]
    public void SetUp()
    {
        registrations.Clear();
        authorizer = Substitute.For<IOperatorRequestAuthorizer>();
        authorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Authorized);
        table = Substitute.For<TableClient>();
        table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(call => GetResponse(call.ArgAt<string>(1)));
        table.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue<IReadOnlyList<Response>>(
                [],
                Substitute.For<Response>()));
        function = new StudentRegistrationAdminFunction(
            NullLogger<StudentRegistrationAdminFunction>.Instance,
            table,
            authorizer);
    }

    [TestCase(OperatorAuthorizationStatus.Unauthenticated, 401)]
    [TestCase(OperatorAuthorizationStatus.Forbidden, 403)]
    public async Task RunAsync_UnauthorizedOperator_DoesNotReadStorage(
        OperatorAuthorizationStatus status,
        int expectedStatus)
    {
        authorizer.Authorize(Arg.Any<HttpRequest>()).Returns(status);

        var result = await function.RunAsync(CreateRequest(HttpMethods.Get));

        Assert.That(
            result,
            Is.TypeOf<StatusCodeResult>()
                .Or.TypeOf<UnauthorizedObjectResult>());
        Assert.That((result as ObjectResult)?.StatusCode ??
            (result as StatusCodeResult)?.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(table.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task RunAsync_InvalidEmail_ReturnsBadRequest()
    {
        var result = await function.RunAsync(
            CreateRequest(HttpMethods.Get, "not-an-email"));

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Get_MissingRegistration_ReturnsNotRegistered()
    {
        var result = await function.RunAsync(CreateRequest(HttpMethods.Get));

        var response = (result as OkObjectResult)?.Value as ApiResponse<object>;
        Assert.That(response?.Success, Is.True);
    }

    [Test]
    public async Task Get_ConsistentPair_ReturnsRegistration()
    {
        SetPair("student@example.com", Guid.NewGuid().ToString("D"));

        var result = await function.RunAsync(CreateRequest(HttpMethods.Get));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
    }

    [Test]
    public async Task Get_InconsistentPair_ReturnsIntegrityError()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        Add(SubscriptionRegistration.CreateEmailIndex(
            "student@example.com",
            subscriptionId));

        var result = await function.RunAsync(CreateRequest(HttpMethods.Get));

        Assert.That(
            (result as ObjectResult)?.StatusCode,
            Is.EqualTo(StatusCodes.Status500InternalServerError));
    }

    [Test]
    public async Task Delete_ConsistentPair_DeletesAtomically()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        SetPair("student@example.com", subscriptionId);

        var result = await function.RunAsync(CreateRequest(HttpMethods.Delete));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await table.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                actions.Count() == 2 &&
                actions.All(action =>
                    action.ActionType == TableTransactionActionType.Delete)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Delete_MissingRegistration_ReturnsNotFound()
    {
        var result = await function.RunAsync(CreateRequest(HttpMethods.Delete));

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Delete_ConcurrentChange_ReturnsConflict()
    {
        SetPair("student@example.com", Guid.NewGuid().ToString("D"));
        table.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response<IReadOnlyList<Response>>>>(
                _ => throw new RequestFailedException(412, "Changed"));

        var result = await function.RunAsync(CreateRequest(HttpMethods.Delete));

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
    }

    [Test]
    public async Task Get_StorageFailure_ReturnsExplicitError()
    {
        table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns<Task<NullableResponse<SubscriptionRegistration>>>(
                _ => throw new RequestFailedException(503, "Unavailable"));

        var result = await function.RunAsync(CreateRequest(HttpMethods.Get));

        Assert.That(
            (result as ObjectResult)?.StatusCode,
            Is.EqualTo(StatusCodes.Status500InternalServerError));
    }

    private static HttpRequest CreateRequest(
        string method,
        string email = "student@example.com")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.QueryString = QueryString.Create("email", email);
        return context.Request;
    }

    private NullableResponse<SubscriptionRegistration> GetResponse(
        string rowKey) =>
        registrations.TryGetValue(rowKey, out var registration)
            ? Response.FromValue(registration, Substitute.For<Response>())
            : AzureTestResponses.Missing<SubscriptionRegistration>();

    private void SetPair(string email, string subscriptionId)
    {
        Add(SubscriptionRegistration.CreateEmailIndex(email, subscriptionId));
        Add(SubscriptionRegistration.CreateSubscriptionIndex(
            email,
            subscriptionId));
    }

    private void Add(SubscriptionRegistration registration) =>
        registrations[registration.RowKey] = registration;
}
