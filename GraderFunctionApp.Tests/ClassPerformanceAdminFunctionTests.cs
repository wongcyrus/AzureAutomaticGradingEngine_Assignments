using System.Linq.Expressions;
using System.Text.Json;
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

public class ClassPerformanceAdminFunctionTests
{
    private const string OwnerEmail = "teacher@example.com";
    private const string StudentEmail = "student@example.com";
    private readonly string classId = Guid.NewGuid().ToString("N");
    private TableClient classes = null!;
    private TableClient memberships = null!;
    private TableClient registrations = null!;
    private TableClient gameStates = null!;
    private TableClient passTests = null!;
    private TableClient failTests = null!;
    private IOperatorRequestAuthorizer authorizer = null!;
    private IRequestAuthenticator authenticator = null!;
    private ClassPerformanceAdminFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        classes = Substitute.For<TableClient>();
        memberships = Substitute.For<TableClient>();
        registrations = Substitute.For<TableClient>();
        gameStates = Substitute.For<TableClient>();
        passTests = Substitute.For<TableClient>();
        failTests = Substitute.For<TableClient>();
        authorizer = Substitute.For<IOperatorRequestAuthorizer>();
        authenticator = Substitute.For<IRequestAuthenticator>();
        authorizer.Authorize(Arg.Any<HttpRequest>())
            .Returns(OperatorAuthorizationStatus.Authorized);
        authenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns(OwnerEmail);
        ConfigureEmptyQueries();
        function = new ClassPerformanceAdminFunction(
            NullLogger<ClassPerformanceAdminFunction>.Instance,
            classes,
            memberships,
            registrations,
            gameStates,
            passTests,
            failTests,
            authorizer,
            authenticator);
    }

    [TestCase(OperatorAuthorizationStatus.Unauthenticated, 401)]
    [TestCase(OperatorAuthorizationStatus.Forbidden, 403)]
    public async Task RunAsync_Unauthorized_DoesNotUseStorage(
        OperatorAuthorizationStatus authorization,
        int expectedStatus)
    {
        authorizer.Authorize(Arg.Any<HttpRequest>()).Returns(authorization);

        var result = await function.RunAsync(
            CreateRequest(HttpMethods.Get, ("action", "classes")));

        Assert.That(
            (result as ObjectResult)?.StatusCode ??
            (result as StatusCodeResult)?.StatusCode,
            Is.EqualTo(expectedStatus));
        Assert.That(classes.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task CreateClass_ValidName_PersistsOwnedClass()
    {
        ClassDefinition? saved = null;
        classes.AddEntityAsync(
                Arg.Do<ClassDefinition>(entity => saved = entity),
                Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response>());

        var result = await function.RunAsync(CreateRequest(
            HttpMethods.Post,
            ("action", "class"),
            ("name", " Cloud 2A ")));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved?.Name, Is.EqualTo("Cloud 2A"));
            Assert.That(saved?.OwnerEmail, Is.EqualTo(OwnerEmail));
            Assert.That(
                saved?.PartitionKey,
                Is.EqualTo(ClassDefinition.OwnerPartition(OwnerEmail)));
        }
    }

    [Test]
    public async Task CreateClass_EmptyName_ReturnsBadRequest()
    {
        var result = await function.RunAsync(CreateRequest(
            HttpMethods.Post,
            ("action", "class"),
            ("name", " ")));

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        await classes.DidNotReceive().AddEntityAsync(
            Arg.Any<ClassDefinition>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListClasses_ReturnsRosterCount()
    {
        ConfigureClassQuery(CreateClass());
        ConfigureMembershipQuery(new ClassMembership
        {
            PartitionKey = classId,
            RowKey = ClassDefinition.StudentRowKey(StudentEmail),
            Email = StudentEmail
        });

        var result = await function.RunAsync(
            CreateRequest(HttpMethods.Get, ("action", "classes")));
        var json = SerializeResult(result);

        Assert.That(json, Does.Contain("\"studentCount\":1"));
        Assert.That(json, Does.Contain("\"name\":\"Cloud 2A\""));
    }

    [Test]
    public async Task ImportRoster_ValidEmails_UpsertsOneTransaction()
    {
        ConfigureOwnedClass();
        IReadOnlyList<TableTransactionAction>? savedActions = null;
        memberships.SubmitTransactionAsync(
                Arg.Do<IEnumerable<TableTransactionAction>>(actions =>
                    savedActions = actions.ToList()),
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue<IReadOnlyList<Response>>(
                [],
                Substitute.For<Response>()));
        classes.UpdateEntityAsync(
                Arg.Any<ClassDefinition>(),
                Arg.Any<ETag>(),
                TableUpdateMode.Replace,
                Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response>());

        var result = await function.RunAsync(CreateRequest(
            HttpMethods.Post,
            ("action", "roster"),
            ("classId", classId),
            ("emails", " Student@Example.com |second@example.com")));

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        Assert.That(savedActions, Has.Count.EqualTo(2));
        Assert.That(
            savedActions!.Select(action =>
                ((ClassMembership)action.Entity).Email),
            Is.EquivalentTo(new[]
            {
                StudentEmail,
                "second@example.com"
            }));
    }

    [Test]
    public async Task Performance_AggregatesAuthoritativeStudentData()
    {
        ConfigureOwnedClass();
        ConfigureMembershipQuery(new ClassMembership
        {
            PartitionKey = classId,
            RowKey = ClassDefinition.StudentRowKey(StudentEmail),
            Email = StudentEmail
        });
        var registration = SubscriptionRegistration.CreateEmailIndex(
            StudentEmail,
            Guid.NewGuid().ToString("D"));
        registrations.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                SubscriptionRegistration.EmailRowKey(StudentEmail),
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                registration,
                Substitute.For<Response>()));
        var subscriptionIndex = SubscriptionRegistration.CreateSubscriptionIndex(
            StudentEmail,
            registration.SubscriptionId);
        var subscriptionResponse = Response.FromValue(
            subscriptionIndex,
            Substitute.For<Response>());
        registrations.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                SubscriptionRegistration.SubscriptionRowKey(
                    registration.SubscriptionId),
                null,
                Arg.Any<CancellationToken>())
            .Returns(subscriptionResponse);
        ConfigureGameStateQuery(new GameState
        {
            PartitionKey = StudentEmail,
            RowKey = "azure-learning-Stella",
            LastUpdated = DateTime.UtcNow,
            HasActiveTask = true
        });
        gameStates.GetEntityIfExistsAsync<GameTaskLock>(
                StudentEmail,
                GameTaskLock.LockRowKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                new GameTaskLock
                {
                    PartitionKey = StudentEmail,
                    TaskName = "Create resource group",
                    Npc = "Stella",
                    Game = "azure-learning"
                },
                Substitute.For<Response>()));
        ConfigurePassQuery(new PassTestEntity
        {
            PartitionKey = StudentEmail,
            RowKey = "pass",
            TestName = "Test01",
            TaskName = "Create resource group",
            Mark = 10,
            PassedAt = DateTimeOffset.UtcNow
        });
        ConfigureFailQuery(new FailTestEntity
        {
            PartitionKey = StudentEmail,
            RowKey = "failure",
            TestName = "Test02",
            TaskName = "Create resource group",
            FailedAt = DateTimeOffset.UtcNow
        });

        var result = await function.RunAsync(CreateRequest(
            HttpMethods.Get,
            ("action", "performance"),
            ("classId", classId)));
        var json = SerializeResult(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"registeredStudents\":1"));
            Assert.That(json, Does.Contain("\"activeStudents\":1"));
            Assert.That(json, Does.Contain("\"totalMarks\":10"));
            Assert.That(json, Does.Contain("\"failedAttempts\":1"));
            Assert.That(json, Does.Contain("\"completionRate\":0.5"));
        }
    }

    [Test]
    public async Task Performance_UnownedClass_ReturnsNotFound()
    {
        var missing = AzureTestResponses.Missing<ClassDefinition>();
        classes.GetEntityIfExistsAsync<ClassDefinition>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(missing);

        var result = await function.RunAsync(CreateRequest(
            HttpMethods.Get,
            ("action", "performance"),
            ("classId", classId)));

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    private void ConfigureEmptyQueries()
    {
        ConfigureClassQuery();
        ConfigureMembershipQuery();
        ConfigureGameStateQuery();
        ConfigurePassQuery();
        ConfigureFailQuery();
        var missingRegistration =
            AzureTestResponses.Missing<SubscriptionRegistration>();
        registrations.GetEntityIfExistsAsync<SubscriptionRegistration>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(missingRegistration);
        var missingLock = AzureTestResponses.Missing<GameTaskLock>();
        gameStates.GetEntityIfExistsAsync<GameTaskLock>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(missingLock);
    }

    private void ConfigureOwnedClass()
    {
        classes.GetEntityIfExistsAsync<ClassDefinition>(
                ClassDefinition.OwnerPartition(OwnerEmail),
                classId,
                null,
                Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                CreateClass(),
                Substitute.For<Response>()));
    }

    private ClassDefinition CreateClass() => new()
    {
        PartitionKey = ClassDefinition.OwnerPartition(OwnerEmail),
        RowKey = classId,
        OwnerEmail = OwnerEmail,
        Name = "Cloud 2A",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        ETag = new ETag("\"class\"")
    };

    private void ConfigureClassQuery(params ClassDefinition[] values)
    {
        var result = AzureTestResponses.AsyncPageable(values);
        classes.QueryAsync(
                Arg.Any<Expression<Func<ClassDefinition, bool>>>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private void ConfigureMembershipQuery(params ClassMembership[] values)
    {
        var result = AzureTestResponses.AsyncPageable(values);
        memberships.QueryAsync(
                Arg.Any<Expression<Func<ClassMembership, bool>>>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private void ConfigureGameStateQuery(params GameState[] values)
    {
        var result = AzureTestResponses.AsyncPageable(values);
        gameStates.QueryAsync(
                Arg.Any<Expression<Func<GameState, bool>>>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private void ConfigurePassQuery(params PassTestEntity[] values)
    {
        var result = AzureTestResponses.AsyncPageable(values);
        passTests.QueryAsync(
                Arg.Any<Expression<Func<PassTestEntity, bool>>>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private void ConfigureFailQuery(params FailTestEntity[] values)
    {
        var result = AzureTestResponses.AsyncPageable(values);
        failTests.QueryAsync(
                Arg.Any<Expression<Func<FailTestEntity, bool>>>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private static HttpRequest CreateRequest(
        string method,
        params (string Name, string Value)[] parameters)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.QueryString = QueryString.Create(
            parameters.Select(parameter =>
                new KeyValuePair<string, string?>(
                    parameter.Name,
                    parameter.Value)));
        return context.Request;
    }

    private static string SerializeResult(IActionResult result)
    {
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        return JsonSerializer.Serialize(((OkObjectResult)result).Value);
    }
}
