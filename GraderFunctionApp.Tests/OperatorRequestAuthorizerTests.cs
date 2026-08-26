using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GraderFunctionApp.Tests;

public class OperatorRequestAuthorizerTests
{
    private IRequestAuthenticator requestAuthenticator = null!;

    [SetUp]
    public void SetUp()
    {
        requestAuthenticator = Substitute.For<IRequestAuthenticator>();
    }

    [Test]
    public void Authorize_ConfiguredOperator_IsAuthorized()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("second@example.com");
        var authorizer = CreateAuthorizer(
            " First@Example.com ; second@example.com\nthird@example.com ");

        var result = authorizer.Authorize(new DefaultHttpContext().Request);

        Assert.That(result, Is.EqualTo(OperatorAuthorizationStatus.Authorized));
    }

    [Test]
    public void Authorize_SignedStudent_IsForbidden()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("student@example.com");
        var authorizer = CreateAuthorizer("operator@example.com");

        var result = authorizer.Authorize(new DefaultHttpContext().Request);

        Assert.That(result, Is.EqualTo(OperatorAuthorizationStatus.Forbidden));
    }

    [Test]
    public void Authorize_MissingSignedIdentity_IsUnauthenticated()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns((string?)null);
        var authorizer = CreateAuthorizer("operator@example.com");

        var result = authorizer.Authorize(new DefaultHttpContext().Request);

        Assert.That(
            result,
            Is.EqualTo(OperatorAuthorizationStatus.Unauthenticated));
    }

    [Test]
    public void Authorize_MissingAllowlist_FailsClosed()
    {
        requestAuthenticator.GetAuthenticatedEmail(Arg.Any<HttpRequest>())
            .Returns("operator@example.com");
        var authorizer = CreateAuthorizer(null);

        var result = authorizer.Authorize(new DefaultHttpContext().Request);

        Assert.That(result, Is.EqualTo(OperatorAuthorizationStatus.Forbidden));
    }

    private OperatorRequestAuthorizer CreateAuthorizer(string? adminEmails)
    {
        var values = new Dictionary<string, string?>();
        if (adminEmails is not null)
        {
            values["ADMIN_EMAILS"] = adminEmails;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new OperatorRequestAuthorizer(
            configuration,
            requestAuthenticator,
            NullLogger<OperatorRequestAuthorizer>.Instance);
    }
}
