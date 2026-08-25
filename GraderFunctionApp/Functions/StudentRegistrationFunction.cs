using System.Net;
using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Configuration;
using GraderFunctionApp.Interfaces;
using GraderFunctionApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraderFunctionApp.Functions;

public class StudentRegistrationFunction
{
    private readonly ILogger<StudentRegistrationFunction> _logger;
    private readonly TableServiceClient _tableServiceClient;
    private readonly Func<string, string, Task<bool>> _hasStudentSubscriptionAccessAsync;
    private readonly IRequestAuthenticator _requestAuthenticator;
    private readonly StorageOptions _storageOptions;

    private const string EmailConflictMessage =
        "A subscription registration already exists for this account.";
    private const string SubscriptionConflictMessage =
        "This Azure subscription is already registered.";
    private const string IntegrityErrorMessage =
        "Subscription registration data is inconsistent. Contact an administrator.";
    private const int RegistrationStateReadAttempts = 3;

    public StudentRegistrationFunction(
        ILogger<StudentRegistrationFunction> logger,
        TableServiceClient tableServiceClient,
        IOptions<StorageOptions> storageOptions,
        IRequestAuthenticator requestAuthenticator)
        : this(
            logger,
            tableServiceClient,
            storageOptions,
            Services.Azure.HasStudentSubscriptionAccessAsync,
            requestAuthenticator)
    {
    }

    internal StudentRegistrationFunction(
        ILogger<StudentRegistrationFunction> logger,
        TableServiceClient tableServiceClient,
        IOptions<StorageOptions> storageOptions,
        Func<string, string, Task<bool>> hasStudentSubscriptionAccessAsync,
        IRequestAuthenticator requestAuthenticator)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
        _storageOptions = storageOptions.Value;
        _hasStudentSubscriptionAccessAsync = hasStudentSubscriptionAccessAsync;
        _requestAuthenticator = requestAuthenticator;
    }

    [Function(nameof(StudentRegistrationFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        var email = _requestAuthenticator.GetAuthenticatedEmail(req);
        if (email == null)
        {
            return new UnauthorizedObjectResult("Authentication required.");
        }

        return req.Method switch
        {
            var method when method == HttpMethods.Get => HandleGet(email),
            var method when method == HttpMethods.Post => await HandlePostAsync(req, email),
            _ => new BadRequestObjectResult("Unsupported HTTP method.")
        };
    }

    private static IActionResult HandleGet(string email)
    {
        email = WebUtility.HtmlEncode(email);
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

    private async Task<IActionResult> HandlePostAsync(
        HttpRequest req,
        string email)
    {
        email = SubscriptionRegistration.NormalizeEmail(email);
        var subscriptionId = req.Form["subscriptionId"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(email) || !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
        {
            return new BadRequestObjectResult("A valid email and Azure subscription ID are required.");
        }

        subscriptionId =
            SubscriptionRegistration.NormalizeSubscriptionId(parsedSubscriptionId);
        _logger.LogInformation(
            "Registering subscription {subscriptionId} for {email}",
            subscriptionId,
            email);

        var table = _tableServiceClient.GetTableClient(
            _storageOptions.SubscriptionRegistrationsTableName);
        await table.CreateIfNotExistsAsync();

        var state = await ReadRegistrationStateAsync(table, email, subscriptionId);
        var existingResult = ResultForExistingState(state, subscriptionId, email);
        if (existingResult is not null)
        {
            return existingResult;
        }

        try
        {
            if (!await _hasStudentSubscriptionAccessAsync(subscriptionId, email))
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

        var emailIndex = SubscriptionRegistration.CreateEmailIndex(
            email,
            subscriptionId);
        var subscriptionIndex = SubscriptionRegistration.CreateSubscriptionIndex(
            email,
            subscriptionId);

        try
        {
            await table.SubmitTransactionAsync(
            [
                new TableTransactionAction(
                    TableTransactionActionType.Add,
                    emailIndex),
                new TableTransactionAction(
                    TableTransactionActionType.Add,
                    subscriptionIndex)
            ]);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status409Conflict)
        {
            _logger.LogInformation(
                "A concurrent subscription registration was detected for {emailHash} and {subscriptionId}",
                emailIndex.RowKey,
                subscriptionId);
            state = await ReadRegistrationStateAsync(table, email, subscriptionId);
            return ResultForExistingState(state, subscriptionId, email) ??
                IntegrityError(
                    "Registration transaction conflicted but no indexes were found for {emailHash} and {subscriptionId}",
                    emailIndex.RowKey,
                    subscriptionId);
        }

        return new OkObjectResult(
            $"Thank you {email}, subscription {subscriptionId} is registered for managed-identity grading.");
    }

    private async Task<RegistrationState> ReadRegistrationStateAsync(
        TableClient table,
        string email,
        string subscriptionId)
    {
        for (var attempt = 1; attempt <= RegistrationStateReadAttempts; attempt++)
        {
            var state = await ReadRegistrationStateOnceAsync(
                table,
                email,
                subscriptionId);
            if (state != RegistrationState.IntegrityError ||
                attempt == RegistrationStateReadAttempts)
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt));
        }

        throw new InvalidOperationException("Registration state retry loop exited unexpectedly.");
    }

    private static async Task<RegistrationState> ReadRegistrationStateOnceAsync(
        TableClient table,
        string email,
        string subscriptionId)
    {
        var emailRowKey = SubscriptionRegistration.EmailRowKey(email);
        var subscriptionRowKey =
            SubscriptionRegistration.SubscriptionRowKey(subscriptionId);
        var emailResponse =
            await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                emailRowKey);
        var subscriptionResponse =
            await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                subscriptionRowKey);
        var emailIndex = emailResponse.HasValue ? emailResponse.Value : null;
        var subscriptionIndex =
            subscriptionResponse.HasValue ? subscriptionResponse.Value : null;

        if (emailIndex is not null &&
            !IsValidEmailIndex(emailIndex, emailRowKey, email))
        {
            return RegistrationState.IntegrityError;
        }
        if (subscriptionIndex is not null &&
            !IsValidSubscriptionIndex(
                subscriptionIndex,
                subscriptionRowKey,
                subscriptionId))
        {
            return RegistrationState.IntegrityError;
        }

        if (emailIndex is not null &&
            emailIndex.SubscriptionId != subscriptionId)
        {
            if (subscriptionIndex?.Email == email)
            {
                return RegistrationState.IntegrityError;
            }

            var counterpart =
                await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                    SubscriptionRegistration.Partition,
                    SubscriptionRegistration.SubscriptionRowKey(
                        emailIndex.SubscriptionId));
            return counterpart.HasValue &&
                IsValidSubscriptionIndex(
                    counterpart.Value!,
                    SubscriptionRegistration.SubscriptionRowKey(
                        emailIndex.SubscriptionId),
                    emailIndex.SubscriptionId) &&
                counterpart.Value!.Email == email
                    ? RegistrationState.EmailConflict
                    : RegistrationState.IntegrityError;
        }

        if (subscriptionIndex is not null &&
            subscriptionIndex.Email != email)
        {
            if (emailIndex?.SubscriptionId == subscriptionId)
            {
                return RegistrationState.IntegrityError;
            }

            var counterpart =
                await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                    SubscriptionRegistration.Partition,
                    SubscriptionRegistration.EmailRowKey(
                        subscriptionIndex.Email));
            return counterpart.HasValue &&
                IsValidEmailIndex(
                    counterpart.Value!,
                    SubscriptionRegistration.EmailRowKey(
                        subscriptionIndex.Email),
                    subscriptionIndex.Email) &&
                counterpart.Value!.SubscriptionId == subscriptionId
                    ? RegistrationState.SubscriptionConflict
                    : RegistrationState.IntegrityError;
        }

        return (emailIndex, subscriptionIndex) switch
        {
            (null, null) => RegistrationState.Available,
            (not null, not null) => RegistrationState.Idempotent,
            _ => RegistrationState.IntegrityError
        };
    }

    private IActionResult? ResultForExistingState(
        RegistrationState state,
        string subscriptionId,
        string email) =>
        state switch
        {
            RegistrationState.Available => null,
            RegistrationState.Idempotent => new OkObjectResult(
                $"Subscription {subscriptionId} is already registered for {email}."),
            RegistrationState.EmailConflict =>
                new ConflictObjectResult(EmailConflictMessage),
            RegistrationState.SubscriptionConflict =>
                new ConflictObjectResult(SubscriptionConflictMessage),
            RegistrationState.IntegrityError => IntegrityError(
                "Subscription registration indexes are inconsistent for {emailHash} and {subscriptionId}",
                SubscriptionRegistration.EmailRowKey(email),
                subscriptionId),
            _ => throw new InvalidOperationException(
                $"Unsupported registration state: {state}")
        };

    private ObjectResult IntegrityError(
        string message,
        params object?[] arguments)
    {
        _logger.LogError(message, arguments);
        return new ObjectResult(IntegrityErrorMessage)
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }

    private static bool IsValidEmailIndex(
        SubscriptionRegistration index,
        string expectedRowKey,
        string expectedEmail) =>
        index.PartitionKey == SubscriptionRegistration.Partition &&
        index.RowKey == expectedRowKey &&
        index.IndexKind == SubscriptionRegistration.EmailIndexKind &&
        index.Email == expectedEmail &&
        Guid.TryParse(index.SubscriptionId, out var subscriptionId) &&
        index.SubscriptionId ==
            SubscriptionRegistration.NormalizeSubscriptionId(subscriptionId);

    private static bool IsValidSubscriptionIndex(
        SubscriptionRegistration index,
        string expectedRowKey,
        string expectedSubscriptionId) =>
        index.PartitionKey == SubscriptionRegistration.Partition &&
        index.RowKey == expectedRowKey &&
        index.IndexKind == SubscriptionRegistration.SubscriptionIndexKind &&
        index.SubscriptionId == expectedSubscriptionId &&
        !string.IsNullOrWhiteSpace(index.Email) &&
        index.Email == SubscriptionRegistration.NormalizeEmail(index.Email);

    private enum RegistrationState
    {
        Available,
        Idempotent,
        EmailConflict,
        SubscriptionConflict,
        IntegrityError
    }
}
