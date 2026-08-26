using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class StudentRegistrationFunctionTests
{
    private readonly Dictionary<string, SubscriptionRegistration> registrations =
        new(StringComparer.Ordinal);
    private StudentRegistrationFunction function = null!;
    private TableClient tableClient = null!;
    private Func<string, string, Task<bool>> hasAccess = null!;
    private IRequestAuthenticator requestAuthenticator = null!;

    [SetUp]
    public void SetUp()
    {
        registrations.Clear();
        tableClient = Substitute.For<TableClient>();
        var tableServiceClient = Substitute.For<TableServiceClient>();
        tableServiceClient.GetTableClient("SubscriptionRegistrations")
            .Returns(tableClient);
        tableClient.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(call => RegistrationResponse(call.ArgAt<string>(1)));
        tableClient.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue<IReadOnlyList<Response>>(
                [],
                Substitute.For<Response>()));
        hasAccess = Substitute.For<Func<string, string, Task<bool>>>();
        hasAccess(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("student@example.com");
        function = new StudentRegistrationFunction(
            NullLogger<StudentRegistrationFunction>.Instance,
            tableServiceClient,
            Options.Create(new StorageOptions()),
            hasAccess,
            requestAuthenticator);
    }

    [Test]
    public void PublicConstructor_CreatesFunction()
    {
        var instance = new StudentRegistrationFunction(
            NullLogger<StudentRegistrationFunction>.Instance,
            Substitute.For<TableServiceClient>(),
            Options.Create(new StorageOptions()),
            Substitute.For<IRequestAuthenticator>());

        Assert.That(instance, Is.Not.Null);
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
        context.Request.QueryString =
            QueryString.Create("email", "<student@example.com>");

        var result = await function.RunAsync(context.Request);

        var content = result as ContentResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content?.StatusCode, Is.EqualTo(200));
            Assert.That(content?.ContentType, Is.EqualTo("text/html"));
            Assert.That(
                content?.Content,
                Does.Contain("value=\"student@example.com\""));
            Assert.That(
                content?.Content,
                Does.Not.Contain("&lt;student@example.com&gt;"));
        }
    }

    [Test]
    public async Task RunAsync_PostWithInvalidForm_ReturnsBadRequest()
    {
        var result = await function.RunAsync(CreatePostRequest(
            "victim@example.com",
            "not-a-guid"));

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
    public async Task RunAsync_ValidPost_AddsBothIndexesAtomically()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns(" Student@Example.COM ");
        var subscriptionId = Guid.NewGuid();

        var result = await function.RunAsync(CreatePostRequest(
            "victim@example.com",
            $" {subscriptionId:D} "));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await hasAccess.Received(1)(
            subscriptionId.ToString("D"),
            "student@example.com");
        await tableClient.Received(1).SubmitTransactionAsync(
            Arg.Is<IEnumerable<TableTransactionAction>>(actions =>
                IsExpectedAddTransaction(
                    actions,
                    "student@example.com",
                    subscriptionId.ToString("D"))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_ExactPairAlreadyExists_IsIdempotent()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        SetPair("student@example.com", subscriptionId);

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await hasAccess.DidNotReceive()(Arg.Any<string>(), Arg.Any<string>());
        await tableClient.DidNotReceive().SubmitTransactionAsync(
            Arg.Any<IEnumerable<TableTransactionAction>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_EmailRegisteredElsewhere_ReturnsStableConflict()
    {
        var existingSubscription = Guid.NewGuid().ToString("D");
        SetPair("student@example.com", existingSubscription);

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            Guid.NewGuid().ToString("D")));

        AssertConflict(
            result,
            "A subscription registration already exists for this account.",
            existingSubscription);
    }

    [Test]
    public async Task RunAsync_SubscriptionRegisteredElsewhere_ReturnsStableConflict()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        SetPair("owner@example.com", subscriptionId);

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        AssertConflict(
            result,
            "This Azure subscription is already registered.",
            "owner@example.com");
    }

    [Test]
    public async Task RunAsync_PartialIndexes_ReturnsIntegrityError()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        Add(SubscriptionRegistration.CreateEmailIndex(
            "student@example.com",
            subscriptionId));

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        AssertIntegrityError(result);
    }

    [Test]
    public async Task RunAsync_DisagreeingIndexes_ReturnsIntegrityError()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        Add(SubscriptionRegistration.CreateEmailIndex(
            "student@example.com",
            subscriptionId));
        Add(SubscriptionRegistration.CreateSubscriptionIndex(
            "other@example.com",
            subscriptionId));

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        AssertIntegrityError(result);
    }

    [TestCase("partition")]
    [TestCase("row")]
    [TestCase("kind")]
    [TestCase("email")]
    [TestCase("guid")]
    [TestCase("normalized-guid")]
    public async Task RunAsync_InvalidEmailIndex_ReturnsIntegrityError(
        string invalidField)
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        var expectedRowKey =
            SubscriptionRegistration.EmailRowKey("student@example.com");
        var emailIndex = SubscriptionRegistration.CreateEmailIndex(
            "student@example.com",
            subscriptionId);
        switch (invalidField)
        {
            case "partition":
                emailIndex.PartitionKey = "wrong";
                break;
            case "row":
                emailIndex.RowKey = "wrong";
                break;
            case "kind":
                emailIndex.IndexKind =
                    SubscriptionRegistration.SubscriptionIndexKind;
                break;
            case "email":
                emailIndex.Email = "other@example.com";
                break;
            case "guid":
                emailIndex.SubscriptionId = "not-a-guid";
                break;
            case "normalized-guid":
                emailIndex.SubscriptionId = subscriptionId.ToUpperInvariant();
                break;
        }
        registrations[expectedRowKey] = emailIndex;
        Add(SubscriptionRegistration.CreateSubscriptionIndex(
            "student@example.com",
            subscriptionId));

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        AssertIntegrityError(result);
    }

    [TestCase("partition")]
    [TestCase("row")]
    [TestCase("kind")]
    [TestCase("subscription")]
    [TestCase("empty-email")]
    [TestCase("normalized-email")]
    public async Task RunAsync_InvalidSubscriptionIndex_ReturnsIntegrityError(
        string invalidField)
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        var expectedRowKey =
            SubscriptionRegistration.SubscriptionRowKey(subscriptionId);
        var subscriptionIndex =
            SubscriptionRegistration.CreateSubscriptionIndex(
                "student@example.com",
                subscriptionId);
        switch (invalidField)
        {
            case "partition":
                subscriptionIndex.PartitionKey = "wrong";
                break;
            case "row":
                subscriptionIndex.RowKey = "wrong";
                break;
            case "kind":
                subscriptionIndex.IndexKind =
                    SubscriptionRegistration.EmailIndexKind;
                break;
            case "subscription":
                subscriptionIndex.SubscriptionId =
                    Guid.NewGuid().ToString("D");
                break;
            case "empty-email":
                subscriptionIndex.Email = " ";
                break;
            case "normalized-email":
                subscriptionIndex.Email = "Student@Example.COM";
                break;
        }
        Add(SubscriptionRegistration.CreateEmailIndex(
            "student@example.com",
            subscriptionId));
        registrations[expectedRowKey] = subscriptionIndex;

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        AssertIntegrityError(result);
    }

    [Test]
    public async Task RunAsync_NonSnapshotIndexRead_RetriesBeforeReportingIntegrityError()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        SetPair("student@example.com", subscriptionId);
        var readCount = 0;
        tableClient.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                readCount++;
                return readCount == 2
                    ? AzureTestResponses.Missing<SubscriptionRegistration>()
                    : RegistrationResponse(call.ArgAt<string>(1));
            });

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        Assert.That(readCount, Is.EqualTo(4));
        await hasAccess.DidNotReceive()(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task RunAsync_SubscriptionNotBoundToEmail_ReturnsForbidden()
    {
        hasAccess(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            Guid.NewGuid().ToString("D")));

        Assert.That(result, Is.TypeOf<ObjectResult>()
            .With.Property(nameof(ObjectResult.StatusCode))
            .EqualTo(StatusCodes.Status403Forbidden));
        await tableClient.DidNotReceive().SubmitTransactionAsync(
            Arg.Any<IEnumerable<TableTransactionAction>>(),
            Arg.Any<CancellationToken>());
    }

    [TestCase(401)]
    [TestCase(403)]
    [TestCase(404)]
    public async Task RunAsync_GraderCannotAccessResourceGroup_ReturnsForbidden(
        int status)
    {
        hasAccess(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<bool>>(
                _ => throw new RequestFailedException(status, "Access failed"));

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            Guid.NewGuid().ToString("D")));

        Assert.That(result, Is.TypeOf<ObjectResult>()
            .With.Property(nameof(ObjectResult.StatusCode))
            .EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task RunAsync_ConcurrentSamePair_IsIdempotent()
    {
        var subscriptionId = Guid.NewGuid().ToString("D");
        tableClient.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response<IReadOnlyList<Response>>>>(_ =>
            {
                SetPair("student@example.com", subscriptionId);
                throw new RequestFailedException(409, "Conflict");
            });

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            subscriptionId));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
    }

    [Test]
    public async Task RunAsync_ConcurrentOtherSubscription_ReturnsEmailConflict()
    {
        var requestedSubscription = Guid.NewGuid().ToString("D");
        var winningSubscription = Guid.NewGuid().ToString("D");
        tableClient.SubmitTransactionAsync(
                Arg.Any<IEnumerable<TableTransactionAction>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Response<IReadOnlyList<Response>>>>(_ =>
            {
                SetPair("student@example.com", winningSubscription);
                throw new RequestFailedException(409, "Conflict");
            });

        var result = await function.RunAsync(CreatePostRequest(
            "ignored@example.com",
            requestedSubscription));

        AssertConflict(
            result,
            "A subscription registration already exists for this account.",
            winningSubscription);
    }

    private NullableResponse<SubscriptionRegistration> RegistrationResponse(
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

    private static bool IsExpectedAddTransaction(
        IEnumerable<TableTransactionAction> transaction,
        string email,
        string subscriptionId)
    {
        var actions = transaction.ToArray();
        if (actions.Length != 2 ||
            actions.Any(action =>
                action.ActionType != TableTransactionActionType.Add ||
                action.Entity.PartitionKey != SubscriptionRegistration.Partition))
        {
            return false;
        }

        var indexes = actions
            .Select(action => (SubscriptionRegistration)action.Entity)
            .ToArray();
        return indexes.Any(index =>
                index.RowKey == SubscriptionRegistration.EmailRowKey(email) &&
                index.Email == email &&
                index.SubscriptionId == subscriptionId &&
                index.IndexKind == SubscriptionRegistration.EmailIndexKind) &&
            indexes.Any(index =>
                index.RowKey ==
                    SubscriptionRegistration.SubscriptionRowKey(subscriptionId) &&
                index.Email == email &&
                index.SubscriptionId == subscriptionId &&
                index.IndexKind ==
                    SubscriptionRegistration.SubscriptionIndexKind);
    }

    private static void AssertConflict(
        IActionResult result,
        string expectedMessage,
        string secretValue)
    {
        var conflict = result as ConflictObjectResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(conflict?.Value, Is.EqualTo(expectedMessage));
            Assert.That(conflict?.Value?.ToString(), Does.Not.Contain(secretValue));
        }
    }

    private static void AssertIntegrityError(IActionResult result)
    {
        var error = result as ObjectResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                error?.StatusCode,
                Is.EqualTo(StatusCodes.Status500InternalServerError));
            Assert.That(
                error?.Value,
                Is.EqualTo(
                    "Subscription registration data is inconsistent. Contact an administrator."));
        }
    }

    private static HttpRequest CreatePostRequest(
        string email,
        string subscriptionId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["email"] = email,
                ["subscriptionId"] = subscriptionId
            });
        return context.Request;
    }
}
