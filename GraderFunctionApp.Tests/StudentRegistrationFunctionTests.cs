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

public class StudentRegistrationFunctionTests
{
    private StudentRegistrationFunction function = null!;
    private TableClient tableClient = null!;
    private Func<string, string, Task<bool>> hasAccess = null!;
    private IRequestAuthenticator requestAuthenticator = null!;

    [SetUp]
    public void SetUp()
    {
        tableClient = Substitute.For<TableClient>();
        var tableServiceClient = Substitute.For<TableServiceClient>();
        tableServiceClient.GetTableClient("Subscription").Returns(tableClient);
        hasAccess = Substitute.For<Func<string, string, Task<bool>>>();
        hasAccess(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("student@example.com");
        function = new StudentRegistrationFunction(
            NullLogger<StudentRegistrationFunction>.Instance,
            tableServiceClient,
            hasAccess,
            requestAuthenticator);
    }

    [Test]
    public async Task RunAsync_MissingSignedIdentity_ReturnsUnauthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;

        var result = await function.RunAsync(context.Request);

        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task RunAsync_Get_IgnoresUntrustedQueryEmail()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = QueryString.Create("email", "<student@example.com>");

        var result = await function.RunAsync(context.Request);

        var content = result as ContentResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content?.StatusCode, Is.EqualTo(200));
            Assert.That(content?.ContentType, Is.EqualTo("text/html"));
            Assert.That(content?.Content, Does.Contain("value=\"student@example.com\""));
            Assert.That(content?.Content, Does.Not.Contain("&lt;student@example.com&gt;"));
        }
    }

    [Test]
    public async Task RunAsync_PostWithInvalidForm_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["email"] = "victim@example.com",
            ["subscriptionId"] = "not-a-guid"
        });

        var result = await function.RunAsync(context.Request);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunAsync_UnsupportedMethod_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;

        var result = await function.RunAsync(context.Request);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunAsync_ValidPost_RegistersNormalizedSubscription()
    {
        ReturnRegistration(null);
        var subscriptionId = Guid.NewGuid();

        var result = await function.RunAsync(CreatePostRequest(
            "victim@example.com", $" {subscriptionId:D} "));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await hasAccess.Received(1)(subscriptionId.ToString(), "student@example.com");
        await tableClient.Received(1).AddEntityAsync(
            Arg.Is<Subscription>(entity =>
                entity.PartitionKey == "student@example.com" &&
                entity.RowKey == Subscription.RegistrationRowKey &&
                entity.SubscriptionId == subscriptionId.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_SubscriptionNotBoundToEmail_ReturnsForbidden()
    {
        hasAccess(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await function.RunAsync(CreatePostRequest(
            "student@example.com", Guid.NewGuid().ToString()));

        Assert.That(result, Is.TypeOf<ObjectResult>()
            .With.Property(nameof(ObjectResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
        await tableClient.DidNotReceive().AddEntityAsync(
            Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
    }

    [TestCase(401)]
    [TestCase(403)]
    [TestCase(404)]
    public async Task RunAsync_AuthorizationLookupFails_ReturnsForbidden(int status)
    {
        hasAccess(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<bool>>(_ => throw new RequestFailedException(status, "Access failed"));

        var result = await function.RunAsync(CreatePostRequest(
            "student@example.com", Guid.NewGuid().ToString()));

        Assert.That(result, Is.TypeOf<ObjectResult>()
            .With.Property(nameof(ObjectResult.StatusCode)).EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task RunAsync_SameSubscriptionAlreadyRegistered_ReturnsOk()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        ReturnRegistration(new Subscription { SubscriptionId = subscriptionId });

        var result = await function.RunAsync(CreatePostRequest(
            "student@example.com", subscriptionId));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await tableClient.DidNotReceive().AddEntityAsync(
            Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_DifferentSubscriptionAlreadyRegistered_ReturnsConflict()
    {
        ReturnRegistration(new Subscription { SubscriptionId = Guid.NewGuid().ToString() });

        var result = await function.RunAsync(CreatePostRequest(
            "student@example.com", Guid.NewGuid().ToString()));

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
    }

    [Test]
    public async Task RunAsync_ConcurrentRegistration_ReturnsConflict()
    {
        ReturnRegistration(null);
        tableClient.AddEntityAsync(
                Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(409, "Conflict"));

        var result = await function.RunAsync(CreatePostRequest(
            "student@example.com", Guid.NewGuid().ToString()));

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
    }

    private static HttpRequest CreatePostRequest(string email, string subscriptionId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["email"] = email,
            ["subscriptionId"] = subscriptionId
        });
        return context.Request;
    }

    private void ReturnRegistration(Subscription? registration)
    {
        var response = Substitute.For<NullableResponse<Subscription>>();
        response.HasValue.Returns(registration is not null);
        if (registration is not null)
        {
            response.Value.Returns(registration);
        }
        tableClient.GetEntityIfExistsAsync<Subscription>(
                Arg.Any<string>(),
                Subscription.RegistrationRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(response);
    }
}
