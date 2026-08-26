using System.Net.Mail;
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

public class StudentRegistrationAdminFunction
{
    private readonly ILogger<StudentRegistrationAdminFunction> logger;
    private readonly IOperatorRequestAuthorizer operatorRequestAuthorizer;
    private readonly TableClient table;

    public StudentRegistrationAdminFunction(
        ILogger<StudentRegistrationAdminFunction> logger,
        TableServiceClient tableServiceClient,
        IOptions<StorageOptions> storageOptions,
        IOperatorRequestAuthorizer operatorRequestAuthorizer)
        : this(
            logger,
            tableServiceClient.GetTableClient(
                storageOptions.Value.SubscriptionRegistrationsTableName),
            operatorRequestAuthorizer)
    {
    }

    internal StudentRegistrationAdminFunction(
        ILogger<StudentRegistrationAdminFunction> logger,
        TableClient table,
        IOperatorRequestAuthorizer operatorRequestAuthorizer)
    {
        this.logger = logger;
        this.table = table;
        this.operatorRequestAuthorizer = operatorRequestAuthorizer;
    }

    [Function(nameof(StudentRegistrationAdminFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            "delete",
            Route = "operator/subscription-registration")]
        HttpRequest request)
    {
        var authorizationResult = Authorize(request);
        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        var emailValue = request.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(emailValue) ||
            !MailAddress.TryCreate(emailValue.Trim(), out _))
        {
            return new BadRequestObjectResult(
                ApiResponse.ErrorResult("A valid student email is required."));
        }

        var email = SubscriptionRegistration.NormalizeEmail(emailValue);
        try
        {
            return request.Method switch
            {
                var method when method == HttpMethods.Get =>
                    await GetRegistrationAsync(email),
                var method when method == HttpMethods.Delete =>
                    await ReleaseRegistrationAsync(email),
                _ => new ObjectResult(
                    ApiResponse.ErrorResult("Unsupported HTTP method."))
                {
                    StatusCode = StatusCodes.Status405MethodNotAllowed
                }
            };
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(
                ex,
                "Subscription registration operator request failed with status {status}.",
                ex.Status);
            return new ObjectResult(
                ApiResponse.ErrorResult(
                    "Subscription registration storage is unavailable."))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task<IActionResult> GetRegistrationAsync(string email)
    {
        var state = await ReadRegistrationAsync(email);
        return state.Status switch
        {
            RegistrationStatus.NotFound => new OkObjectResult(
                ApiResponse<object>.SuccessResult(new
                {
                    email,
                    registered = false
                })),
            RegistrationStatus.Consistent => new OkObjectResult(
                ApiResponse<object>.SuccessResult(new
                {
                    email,
                    registered = true,
                    subscriptionId = state.EmailIndex!.SubscriptionId
                })),
            _ => IntegrityError()
        };
    }

    private async Task<IActionResult> ReleaseRegistrationAsync(string email)
    {
        var state = await ReadRegistrationAsync(email);
        if (state.Status == RegistrationStatus.NotFound)
        {
            return new NotFoundObjectResult(
                ApiResponse.ErrorResult(
                    "No subscription registration exists for that student."));
        }

        if (state.Status != RegistrationStatus.Consistent)
        {
            return IntegrityError();
        }

        try
        {
            await table.SubmitTransactionAsync(
            [
                new TableTransactionAction(
                    TableTransactionActionType.Delete,
                    state.EmailIndex!,
                    state.EmailIndex!.ETag),
                new TableTransactionAction(
                    TableTransactionActionType.Delete,
                    state.SubscriptionIndex!,
                    state.SubscriptionIndex!.ETag)
            ]);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412)
        {
            logger.LogWarning(
                ex,
                "Subscription registration changed during operator release.");
            return new ConflictObjectResult(
                ApiResponse.ErrorResult(
                    "The registration changed concurrently and was not released."));
        }

        return new OkObjectResult(
            ApiResponse<object>.SuccessResult(new
            {
                email,
                released = true,
                subscriptionId = state.EmailIndex!.SubscriptionId
            }));
    }

    private async Task<RegistrationState> ReadRegistrationAsync(string email)
    {
        var emailRowKey = SubscriptionRegistration.EmailRowKey(email);
        var emailResponse =
            await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                emailRowKey);
        if (!emailResponse.HasValue)
        {
            return new(RegistrationStatus.NotFound, null, null);
        }

        var emailIndex = emailResponse.Value!;
        if (!IsValidEmailIndex(emailIndex, email, emailRowKey))
        {
            return new(RegistrationStatus.Inconsistent, emailIndex, null);
        }

        var subscriptionRowKey =
            SubscriptionRegistration.SubscriptionRowKey(
                emailIndex.SubscriptionId);
        var subscriptionResponse =
            await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
                SubscriptionRegistration.Partition,
                subscriptionRowKey);
        if (!subscriptionResponse.HasValue)
        {
            return new(RegistrationStatus.Inconsistent, emailIndex, null);
        }

        var subscriptionIndex = subscriptionResponse.Value!;
        return IsValidSubscriptionIndex(
            subscriptionIndex,
            email,
            emailIndex.SubscriptionId,
            subscriptionRowKey)
            ? new(
                RegistrationStatus.Consistent,
                emailIndex,
                subscriptionIndex)
            : new(
                RegistrationStatus.Inconsistent,
                emailIndex,
                subscriptionIndex);
    }

    private IActionResult? Authorize(HttpRequest request) =>
        operatorRequestAuthorizer.Authorize(request) switch
        {
            OperatorAuthorizationStatus.Authorized => null,
            OperatorAuthorizationStatus.Unauthenticated =>
                new UnauthorizedObjectResult("Authentication required."),
            OperatorAuthorizationStatus.Forbidden =>
                new StatusCodeResult(StatusCodes.Status403Forbidden),
            _ => new StatusCodeResult(StatusCodes.Status403Forbidden)
        };

    private ObjectResult IntegrityError()
    {
        logger.LogError(
            "Operator found inconsistent subscription registration indexes.");
        return new ObjectResult(
            ApiResponse.ErrorResult(
                "Subscription registration data is inconsistent."))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }

    private static bool IsValidEmailIndex(
        SubscriptionRegistration index,
        string email,
        string rowKey) =>
        index.PartitionKey == SubscriptionRegistration.Partition &&
        index.RowKey == rowKey &&
        index.IndexKind == SubscriptionRegistration.EmailIndexKind &&
        index.Email == email &&
        Guid.TryParse(index.SubscriptionId, out var subscriptionId) &&
        index.SubscriptionId ==
            SubscriptionRegistration.NormalizeSubscriptionId(subscriptionId);

    private static bool IsValidSubscriptionIndex(
        SubscriptionRegistration index,
        string email,
        string subscriptionId,
        string rowKey) =>
        index.PartitionKey == SubscriptionRegistration.Partition &&
        index.RowKey == rowKey &&
        index.IndexKind == SubscriptionRegistration.SubscriptionIndexKind &&
        index.Email == email &&
        index.SubscriptionId == subscriptionId;

    private enum RegistrationStatus
    {
        NotFound,
        Consistent,
        Inconsistent
    }

    private sealed record RegistrationState(
        RegistrationStatus Status,
        SubscriptionRegistration? EmailIndex,
        SubscriptionRegistration? SubscriptionIndex);
}
