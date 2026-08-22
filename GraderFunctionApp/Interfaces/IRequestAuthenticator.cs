using Microsoft.AspNetCore.Http;

namespace GraderFunctionApp.Interfaces;

public interface IRequestAuthenticator
{
    string? GetAuthenticatedEmail(HttpRequest request);
}
