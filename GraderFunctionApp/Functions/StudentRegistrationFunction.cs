using System.Net;
using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GraderFunctionApp.Functions;

public class StudentRegistrationFunction
{
    private readonly ILogger<StudentRegistrationFunction> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public StudentRegistrationFunction(
        ILogger<StudentRegistrationFunction> logger,
        TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function(nameof(StudentRegistrationFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        return req.Method switch
        {
            var method when method == HttpMethods.Get => HandleGet(req),
            var method when method == HttpMethods.Post => await HandlePostAsync(req),
            _ => new BadRequestObjectResult("Unsupported HTTP method.")
        };
    }

    private static IActionResult HandleGet(HttpRequest req)
    {
        var email = WebUtility.HtmlEncode(req.Query["email"].ToString());
        var form = $"""
                    <!DOCTYPE html>
                    <html>
                    <body>
                    <form method="post">
                        <label for="email">Email:</label><br>
                        <input type="email" id="email" name="email" size="50" value="{email}" required><br>
                        <label for="subscriptionId">Azure subscription ID:</label><br>
                        <input type="text" id="subscriptionId" name="subscriptionId" size="50" required><br>
                        <button type="submit">Register</button>
                    </form>
                    </body>
                    </html>
                    """;

        return new ContentResult
        {
            Content = form,
            ContentType = "text/html",
            StatusCode = StatusCodes.Status200OK
        };
    }

    private async Task<IActionResult> HandlePostAsync(HttpRequest req)
    {
        var email = req.Form["email"].ToString().Trim().ToLowerInvariant();
        var subscriptionId = req.Form["subscriptionId"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(email) || !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
        {
            return new BadRequestObjectResult("A valid email and Azure subscription ID are required.");
        }

        subscriptionId = parsedSubscriptionId.ToString();
        _logger.LogInformation(
            "Registering subscription {subscriptionId} for {email}",
            subscriptionId,
            email);

        try
        {
            if (!await Services.Azure.HasStudentSubscriptionAccessAsync(subscriptionId, email))
            {
                _logger.LogWarning(
                    "Subscription {subscriptionId} is not bound to {email}",
                    subscriptionId,
                    email);
                return new ObjectResult(
                    "The assignment resource group is not registered to your signed-in email. Rerun the onboarding script with the same email used for Azure Isekai.")
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403 or 404)
        {
            _logger.LogWarning(
                ex,
                "The grading identity cannot access subscription {subscriptionId}",
                subscriptionId);
            return new ObjectResult(
                "The grading identity cannot read this subscription. Run the onboarding script and allow RBAC propagation before registering.")
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        var table = _tableServiceClient.GetTableClient("Subscription");
        await table.CreateIfNotExistsAsync();

        var existing = await table.GetEntityIfExistsAsync<Subscription>(
            email,
            Subscription.RegistrationRowKey);
        if (existing.HasValue && existing.Value is { } registration)
        {
            if (registration.SubscriptionId == subscriptionId)
            {
                return new OkObjectResult(
                    $"Subscription {subscriptionId} is already registered for {email}.");
            }

            return new ConflictObjectResult(
                "An Azure subscription is already registered for this account.");
        }

        try
        {
            await table.AddEntityAsync(new Subscription
            {
                PartitionKey = email,
                RowKey = Subscription.RegistrationRowKey,
                SubscriptionId = subscriptionId
            });
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status409Conflict)
        {
            return new ConflictObjectResult(
                "An Azure subscription was registered concurrently. Reload the page to confirm it.");
        }

        return new OkObjectResult(
            $"Thank you {email}, subscription {subscriptionId} is registered for managed-identity grading.");
    }
}
