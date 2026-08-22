using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraderFunctionApp.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Services;

public sealed class SignedRequestAuthenticator : IRequestAuthenticator
{
    public const string EmailHeader = "X-Grader-Email";
    public const string TimestampHeader = "X-Grader-Timestamp";
    public const string SignatureHeader = "X-Grader-Signature";

    private static readonly TimeSpan MaximumRequestAge = TimeSpan.FromMinutes(5);

    private readonly byte[]? signingKey;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SignedRequestAuthenticator> logger;

    public SignedRequestAuthenticator(
        IConfiguration configuration,
        ILogger<SignedRequestAuthenticator> logger)
        : this(configuration["GRADER_PROXY_SIGNING_KEY"], logger, TimeProvider.System)
    {
    }

    internal SignedRequestAuthenticator(
        string? base64SigningKey,
        ILogger<SignedRequestAuthenticator> logger,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
        signingKey = DecodeSigningKey(base64SigningKey);
    }

    public string? GetAuthenticatedEmail(HttpRequest request)
    {
        if (signingKey is null)
        {
            logger.LogError("GRADER_PROXY_SIGNING_KEY is missing or invalid.");
            return null;
        }

        var email = request.Headers[EmailHeader].ToString()
            .Trim()
            .ToLowerInvariant();
        var timestampText = request.Headers[TimestampHeader].ToString();
        var providedSignature = request.Headers[SignatureHeader].ToString();
        if (string.IsNullOrWhiteSpace(email) ||
            !long.TryParse(
                timestampText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timestampMilliseconds) ||
            string.IsNullOrWhiteSpace(providedSignature))
        {
            return null;
        }

        DateTimeOffset requestTime;
        try
        {
            requestTime = DateTimeOffset.FromUnixTimeMilliseconds(
                timestampMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        if ((timeProvider.GetUtcNow() - requestTime).Duration() >
            MaximumRequestAge)
        {
            return null;
        }

        var canonicalRequest = CreateCanonicalRequest(
            request.Method,
            request.Path,
            request.QueryString,
            timestampText,
            email);
        var expectedSignature = HMACSHA256.HashData(
            signingKey,
            Encoding.UTF8.GetBytes(canonicalRequest));

        byte[] providedSignatureBytes;
        try
        {
            providedSignatureBytes = Convert.FromHexString(providedSignature);
        }
        catch (FormatException)
        {
            return null;
        }

        return providedSignatureBytes.Length == expectedSignature.Length &&
            CryptographicOperations.FixedTimeEquals(
                providedSignatureBytes,
                expectedSignature)
            ? email
            : null;
    }

    internal static string CreateCanonicalRequest(
        string method,
        PathString path,
        QueryString queryString,
        string timestamp,
        string email)
    {
        return string.Join(
            '\n',
            method.ToUpperInvariant(),
            $"{path}{queryString}",
            timestamp,
            email);
    }

    private static byte[]? DecodeSigningKey(string? base64SigningKey)
    {
        if (string.IsNullOrWhiteSpace(base64SigningKey))
        {
            return null;
        }

        try
        {
            var key = Convert.FromBase64String(base64SigningKey);
            return key.Length >= 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
