using Microsoft.AspNetCore.Http;

namespace GraderFunctionApp.Helpers
{
    public static class HttpHelpers
    {
        public static string GetFormValue(HttpRequest req, string key)
        {
            return req.Form[key]!;
        }
        public static string GetQueryValue(HttpRequest req, string key)
        {
            return req.Query[key]!;
        }
    }
}
