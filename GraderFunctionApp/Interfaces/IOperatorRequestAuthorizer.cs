using Microsoft.AspNetCore.Http;

namespace GraderFunctionApp.Interfaces;

public enum OperatorAuthorizationStatus
{
    Authorized,
    Unauthenticated,
    Forbidden
}

public interface IOperatorRequestAuthorizer
{
    OperatorAuthorizationStatus Authorize(HttpRequest request);
}
