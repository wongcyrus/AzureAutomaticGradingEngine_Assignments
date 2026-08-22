using Azure;
using NSubstitute;

namespace GraderFunctionApp.Tests;

internal static class AzureTestResponses
{
    public static AsyncPageable<T> AsyncPageable<T>(params T[] values) where T : notnull
    {
        var page = Page<T>.FromValues(values, null, Substitute.For<Response>());
        return Azure.AsyncPageable<T>.FromPages([page]);
    }

    public static NullableResponse<T> Missing<T>() where T : notnull
    {
        var response = Substitute.For<NullableResponse<T>>();
        response.HasValue.Returns(false);
        return response;
    }
}
