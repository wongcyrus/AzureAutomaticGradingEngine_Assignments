using GraderFunctionApp.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Services;

public sealed class OperatorRequestAuthorizer : IOperatorRequestAuthorizer
{
    private readonly HashSet<string> operatorEmails;
    private readonly IRequestAuthenticator requestAuthenticator;
    private readonly ILogger<OperatorRequestAuthorizer> logger;

    public OperatorRequestAuthorizer(
        IConfiguration configuration,
        IRequestAuthenticator requestAuthenticator,
        ILogger<OperatorRequestAuthorizer> logger)
    {
        this.requestAuthenticator = requestAuthenticator;
        this.logger = logger;
        operatorEmails = (configuration["ADMIN_EMAILS"] ?? string.Empty)
            .Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(static email => email.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    public OperatorAuthorizationStatus Authorize(HttpRequest request)
    {
        var email = requestAuthenticator.GetAuthenticatedEmail(request);
        if (email is null)
        {
            return OperatorAuthorizationStatus.Unauthenticated;
        }

        if (operatorEmails.Contains(email.Trim().ToLowerInvariant()))
        {
            return OperatorAuthorizationStatus.Authorized;
        }

        logger.LogWarning(
            "A signed non-operator identity attempted to access an operator endpoint.");
        return OperatorAuthorizationStatus.Forbidden;
    }
}
