using System.Security.Cryptography;
using System.Text;
using GraderFunctionApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraderFunctionApp.Tests;

public class SignedRequestAuthenticatorTests
{
    private static readonly byte[] SigningKey =
        Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    private SignedRequestAuthenticator authenticator = null!;

    [SetUp]
    public void SetUp()
    {
        authenticator = new SignedRequestAuthenticator(
            Convert.ToBase64String(SigningKey),
            NullLogger<SignedRequestAuthenticator>.Instance,
            new FixedTimeProvider(Now));
    }

    [Test]
    public void GetAuthenticatedEmail_ValidSignature_ReturnsNormalizedEmail()
    {
        var request = CreateSignedRequest(" Student@Example.com ");

        var result = authenticator.GetAuthenticatedEmail(request);

        Assert.That(result, Is.EqualTo("student@example.com"));
    }

    [Test]
    public void GetAuthenticatedEmail_TamperedQuery_ReturnsNull()
    {
        var request = CreateSignedRequest("student@example.com");
        request.QueryString = new QueryString("?game=other&npc=Stella");

        Assert.That(authenticator.GetAuthenticatedEmail(request), Is.Null);
    }

    [Test]
    public void GetAuthenticatedEmail_TamperedEmail_ReturnsNull()
    {
        var request = CreateSignedRequest("student@example.com");
        request.Headers[SignedRequestAuthenticator.EmailHeader] =
            "victim@example.com";

        Assert.That(authenticator.GetAuthenticatedEmail(request), Is.Null);
    }

    [Test]
    public void GetAuthenticatedEmail_ExpiredTimestamp_ReturnsNull()
    {
        var request = CreateSignedRequest(
            "student@example.com",
            Now.AddMinutes(-6));

        Assert.That(authenticator.GetAuthenticatedEmail(request), Is.Null);
    }

    [TestCase("")]
    [TestCase("not-base64")]
    [TestCase("c2hvcnQ=")]
    public void GetAuthenticatedEmail_InvalidSigningKey_ReturnsNull(
        string signingKey)
    {
        var invalidAuthenticator = new SignedRequestAuthenticator(
            signingKey,
            NullLogger<SignedRequestAuthenticator>.Instance,
            new FixedTimeProvider(Now));

        Assert.That(
            invalidAuthenticator.GetAuthenticatedEmail(
                CreateSignedRequest("student@example.com")),
            Is.Null);
    }

    [Test]
    public void GetAuthenticatedEmail_OutOfRangeTimestamp_ReturnsNull()
    {
        var request = CreateSignedRequest("student@example.com");
        request.Headers[SignedRequestAuthenticator.TimestampHeader] =
            long.MaxValue.ToString();

        Assert.That(authenticator.GetAuthenticatedEmail(request), Is.Null);
    }

    private static HttpRequest CreateSignedRequest(
        string email,
        DateTimeOffset? requestTime = null)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        request.Method = HttpMethods.Get;
        request.Path = "/api/GameTaskFunction";
        request.QueryString = new QueryString("?game=azure-learning&npc=Stella");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var timestamp = (requestTime ?? Now)
            .ToUnixTimeMilliseconds()
            .ToString();
        var canonical = string.Join(
            '\n',
            "GET",
            "/api/GameTaskFunction?game=azure-learning&npc=Stella",
            timestamp,
            normalizedEmail);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(
                SigningKey,
                Encoding.UTF8.GetBytes(canonical)));

        request.Headers[SignedRequestAuthenticator.EmailHeader] = email;
        request.Headers[SignedRequestAuthenticator.TimestampHeader] = timestamp;
        request.Headers[SignedRequestAuthenticator.SignatureHeader] = signature;
        return request;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
