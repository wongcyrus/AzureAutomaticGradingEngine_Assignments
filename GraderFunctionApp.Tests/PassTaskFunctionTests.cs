using GraderFunctionApp.Functions;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class PassTaskFunctionTests
{
    private IStorageService storageService = null!;
    private IRequestAuthenticator requestAuthenticator = null!;
    private PassTaskFunction function = null!;

    [SetUp]
    public void SetUp()
    {
        storageService = Substitute.For<IStorageService>();
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("student@example.com");
        function = new PassTaskFunction(
            NullLogger<PassTaskFunction>.Instance,
            storageService,
            requestAuthenticator);
    }

    [Test]
    public async Task Run_MissingAuthentication_ReturnsUnauthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);

        var result = await function.Run(new DefaultHttpContext().Request);

        var response = result as UnauthorizedObjectResult;
        var body = response?.Value as ApiResponse;
        Assert.That(body?.Error, Is.EqualTo("Authentication required."));
    }

    [Test]
    public async Task Run_ReturnsNormalizedStudentsMarks()
    {
        storageService.GetPassedTasksAsync("student@example.com").Returns(new List<(string, int)>
        {
            ("Task A", 10),
            ("Task B", 20)
        });
        var request = CreateRequest("victim@example.com");

        var result = await function.Run(request);

        var json = result as JsonResult;
        var body = json?.Value as ApiResponse<object>;
        Assert.That(body?.Success, Is.True);
        await storageService.Received(1).GetPassedTasksAsync("student@example.com");
    }

    [Test]
    public async Task Run_StorageFailure_ReturnsInternalServerError()
    {
        storageService.GetPassedTasksAsync("student@example.com")
            .Returns<Task<List<(string Name, int Mark)>>>(_ => throw new InvalidOperationException("storage unavailable"));

        var result = await function.Run(CreateRequest("victim@example.com"));

        var response = result as ObjectResult;
        var body = response?.Value as ApiResponse;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response?.StatusCode, Is.EqualTo(500));
            Assert.That(body?.Success, Is.False);
            Assert.That(body?.Details, Is.EqualTo("storage unavailable"));
        }
    }

    private static HttpRequest CreateRequest(string email)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create("email", email);
        return context.Request;
    }
}
