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

public class ClassPerformanceAdminFunction
{
    private const int MaxRosterBatchSize = 100;
    private readonly ILogger<ClassPerformanceAdminFunction> logger;
    private readonly TableClient classes;
    private readonly TableClient memberships;
    private readonly TableClient registrations;
    private readonly TableClient gameStates;
    private readonly TableClient passTests;
    private readonly TableClient failTests;
    private readonly IOperatorRequestAuthorizer authorizer;
    private readonly IRequestAuthenticator requestAuthenticator;

    public ClassPerformanceAdminFunction(
        ILogger<ClassPerformanceAdminFunction> logger,
        TableServiceClient tableServiceClient,
        IOptions<StorageOptions> storageOptions,
        IOperatorRequestAuthorizer authorizer,
        IRequestAuthenticator requestAuthenticator)
        : this(
            logger,
            tableServiceClient.GetTableClient(
                storageOptions.Value.ClassesTableName),
            tableServiceClient.GetTableClient(
                storageOptions.Value.ClassMembershipsTableName),
            tableServiceClient.GetTableClient(
                storageOptions.Value.SubscriptionRegistrationsTableName),
            tableServiceClient.GetTableClient(
                storageOptions.Value.GameStatesTableName),
            tableServiceClient.GetTableClient(
                storageOptions.Value.PassTestTableName),
            tableServiceClient.GetTableClient(
                storageOptions.Value.FailTestTableName),
            authorizer,
            requestAuthenticator)
    {
    }

    internal ClassPerformanceAdminFunction(
        ILogger<ClassPerformanceAdminFunction> logger,
        TableClient classes,
        TableClient memberships,
        TableClient registrations,
        TableClient gameStates,
        TableClient passTests,
        TableClient failTests,
        IOperatorRequestAuthorizer authorizer,
        IRequestAuthenticator requestAuthenticator)
    {
        this.logger = logger;
        this.classes = classes;
        this.memberships = memberships;
        this.registrations = registrations;
        this.gameStates = gameStates;
        this.passTests = passTests;
        this.failTests = failTests;
        this.authorizer = authorizer;
        this.requestAuthenticator = requestAuthenticator;
    }

    [Function(nameof(ClassPerformanceAdminFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", "delete")]
        HttpRequest request)
    {
        var authorizationResult = Authorize(request);
        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        var ownerEmail = requestAuthenticator.GetAuthenticatedEmail(request)!;
        var action = request.Query["action"].ToString().Trim().ToLowerInvariant();

        try
        {
            return (request.Method.ToUpperInvariant(), action) switch
            {
                ("GET", "classes") => await ListClassesAsync(ownerEmail),
                ("POST", "class") => await CreateClassAsync(
                    ownerEmail,
                    request.Query["name"].ToString()),
                ("DELETE", "class") => await DeleteClassAsync(
                    ownerEmail,
                    request.Query["classId"].ToString()),
                ("POST", "roster") => await ImportRosterAsync(
                    ownerEmail,
                    request.Query["classId"].ToString(),
                    request.Query["emails"].ToString()),
                ("DELETE", "member") => await RemoveMemberAsync(
                    ownerEmail,
                    request.Query["classId"].ToString(),
                    request.Query["email"].ToString()),
                ("GET", "performance") => await GetPerformanceAsync(
                    ownerEmail,
                    request.Query["classId"].ToString()),
                ("GET", "student") => await GetStudentAsync(
                    ownerEmail,
                    request.Query["classId"].ToString(),
                    request.Query["email"].ToString()),
                _ => new ObjectResult(ApiResponse.ErrorResult(
                    "Unsupported class administration operation."))
                {
                    StatusCode = StatusCodes.Status405MethodNotAllowed
                }
            };
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(
                ex,
                "Class performance storage operation failed with status {status}.",
                ex.Status);
            return new ObjectResult(ApiResponse.ErrorResult(
                "Class performance storage is unavailable."))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task<IActionResult> ListClassesAsync(string ownerEmail)
    {
        var ownerPartition = ClassDefinition.OwnerPartition(ownerEmail);
        var items = new List<object>();
        await foreach (var classEntity in classes.QueryAsync<ClassDefinition>(
                           entity => entity.PartitionKey == ownerPartition))
        {
            var studentCount = 0;
            await foreach (var _ in memberships.QueryAsync<ClassMembership>(
                               entity => entity.PartitionKey == classEntity.RowKey,
                               maxPerPage: 1000,
                               select: [nameof(ClassMembership.RowKey)]))
            {
                studentCount++;
            }

            items.Add(new
            {
                id = classEntity.RowKey,
                name = classEntity.Name,
                studentCount,
                createdAt = classEntity.CreatedAt,
                updatedAt = classEntity.UpdatedAt
            });
        }

        return Success(new { classes = items });
    }

    private async Task<IActionResult> CreateClassAsync(
        string ownerEmail,
        string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100)
        {
            return BadRequest("Class name must contain 1 to 100 characters.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new ClassDefinition
        {
            PartitionKey = ClassDefinition.OwnerPartition(ownerEmail),
            RowKey = Guid.NewGuid().ToString("N"),
            OwnerEmail = ownerEmail,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now
        };
        await classes.AddEntityAsync(entity);
        return Success(new
        {
            id = entity.RowKey,
            entity.Name,
            studentCount = 0,
            createdAt = entity.CreatedAt,
            updatedAt = entity.UpdatedAt
        });
    }

    private async Task<IActionResult> DeleteClassAsync(
        string ownerEmail,
        string classId)
    {
        var classEntity = await GetOwnedClassAsync(ownerEmail, classId);
        if (classEntity is null)
        {
            return NotFound("Class was not found.");
        }

        var memberEntities = new List<ClassMembership>();
        await foreach (var member in memberships.QueryAsync<ClassMembership>(
                           entity => entity.PartitionKey == classId))
        {
            memberEntities.Add(member);
        }

        foreach (var batch in memberEntities.Chunk(100))
        {
            await memberships.SubmitTransactionAsync(batch.Select(member =>
                new TableTransactionAction(
                    TableTransactionActionType.Delete,
                    member,
                    ETag.All)));
        }

        await classes.DeleteEntityAsync(
            classEntity.PartitionKey,
            classEntity.RowKey,
            classEntity.ETag);
        return Success(new { classId, removedStudents = memberEntities.Count });
    }

    private async Task<IActionResult> ImportRosterAsync(
        string ownerEmail,
        string classId,
        string emailValues)
    {
        var classEntity = await GetOwnedClassAsync(ownerEmail, classId);
        if (classEntity is null)
        {
            return NotFound("Class was not found.");
        }

        var candidates = emailValues
            .Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SubscriptionRegistration.NormalizeEmail)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (candidates.Count is < 1 or > MaxRosterBatchSize)
        {
            return BadRequest(
                $"Each roster import must contain 1 to {MaxRosterBatchSize} emails.");
        }

        var invalid = candidates
            .Where(email => !IsExactEmail(email))
            .ToList();
        if (invalid.Count > 0)
        {
            return BadRequest($"Invalid email address: {invalid[0]}");
        }

        var actions = candidates.Select(email =>
            new TableTransactionAction(
                TableTransactionActionType.UpsertReplace,
                new ClassMembership
                {
                    PartitionKey = classId,
                    RowKey = ClassDefinition.StudentRowKey(email),
                    Email = email,
                    AddedAt = DateTimeOffset.UtcNow
                })).ToList();
        await memberships.SubmitTransactionAsync(actions);

        classEntity.UpdatedAt = DateTimeOffset.UtcNow;
        await classes.UpdateEntityAsync(
            classEntity,
            classEntity.ETag,
            TableUpdateMode.Replace);
        return Success(new { classId, imported = candidates.Count });
    }

    private async Task<IActionResult> RemoveMemberAsync(
        string ownerEmail,
        string classId,
        string emailValue)
    {
        var classEntity = await GetOwnedClassAsync(ownerEmail, classId);
        if (classEntity is null)
        {
            return NotFound("Class was not found.");
        }

        if (!TryNormalizeEmail(emailValue, out var email))
        {
            return BadRequest("A valid student email is required.");
        }

        try
        {
            await memberships.DeleteEntityAsync(
                classId,
                ClassDefinition.StudentRowKey(email),
                ETag.All);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NotFound("Student is not in this class.");
        }

        classEntity.UpdatedAt = DateTimeOffset.UtcNow;
        await classes.UpdateEntityAsync(
            classEntity,
            classEntity.ETag,
            TableUpdateMode.Replace);
        return Success(new { classId, email });
    }

    private async Task<IActionResult> GetPerformanceAsync(
        string ownerEmail,
        string classId)
    {
        var classEntity = await GetOwnedClassAsync(ownerEmail, classId);
        if (classEntity is null)
        {
            return NotFound("Class was not found.");
        }

        var studentEmails = new List<string>();
        await foreach (var member in memberships.QueryAsync<ClassMembership>(
                           entity => entity.PartitionKey == classId))
        {
            studentEmails.Add(member.Email);
        }

        var summaries = await LoadStudentsAsync(studentEmails);
        var taskAnalytics = summaries
            .SelectMany(student => student.TaskAttempts)
            .GroupBy(attempt => attempt.TaskName, StringComparer.Ordinal)
            .Select(group => new
            {
                name = group.Key,
                studentsAttempted = group.Select(item => item.Email).Distinct().Count(),
                passes = group.Sum(item => item.Passes),
                failures = group.Sum(item => item.Failures),
                attempts = group.Sum(item => item.Passes + item.Failures),
                completionRate = group.Sum(item => item.Passes + item.Failures) == 0
                    ? 0
                    : Math.Round(
                        (double)group.Sum(item => item.Passes) /
                        group.Sum(item => item.Passes + item.Failures),
                        4)
            })
            .OrderBy(item => item.name)
            .ToList();

        return Success(new
        {
            @class = new
            {
                id = classEntity.RowKey,
                name = classEntity.Name,
                updatedAt = classEntity.UpdatedAt
            },
            overview = new
            {
                totalStudents = summaries.Count,
                registeredStudents = summaries.Count(item => item.Registered),
                activeStudents = summaries.Count(item => item.ActiveTask is not null),
                totalMarks = summaries.Sum(item => item.TotalMarks),
                averageMarks = summaries.Count == 0
                    ? 0
                    : Math.Round(summaries.Average(item => item.TotalMarks), 2),
                completedTasks = summaries.Sum(item => item.CompletedTaskCount),
                failedAttempts = summaries.Sum(item => item.FailedAttemptCount)
            },
            tasks = taskAnalytics,
            students = summaries.Select(ToStudentResponse)
        });
    }

    private async Task<IActionResult> GetStudentAsync(
        string ownerEmail,
        string classId,
        string emailValue)
    {
        if (await GetOwnedClassAsync(ownerEmail, classId) is null)
        {
            return NotFound("Class was not found.");
        }

        if (!TryNormalizeEmail(emailValue, out var email))
        {
            return BadRequest("A valid student email is required.");
        }

        var membership = await memberships.GetEntityIfExistsAsync<ClassMembership>(
            classId,
            ClassDefinition.StudentRowKey(email));
        if (!membership.HasValue)
        {
            return NotFound("Student is not in this class.");
        }

        var student = await LoadStudentAsync(email);
        return Success(new
        {
            student = ToStudentResponse(student),
            passedTests = student.PassedTests
                .OrderByDescending(item => item.PassedAt)
                .Select(item => new
                {
                    item.TestName,
                    item.TaskName,
                    assignedByNpc = item.AssignedByNPC,
                    item.Mark,
                    item.PassedAt
                }),
            failedAttempts = student.FailedTests
                .OrderByDescending(item => item.FailedAt)
                .Take(100)
                .Select(item => new
                {
                    item.TestName,
                    item.TaskName,
                    assignedByNpc = item.AssignedByNPC,
                    item.FailedAt
                })
        });
    }

    private async Task<List<StudentPerformance>> LoadStudentsAsync(
        IEnumerable<string> emails)
    {
        using var concurrency = new SemaphoreSlim(8);
        var tasks = emails.Select(async email =>
        {
            await concurrency.WaitAsync();
            try
            {
                return await LoadStudentAsync(email);
            }
            finally
            {
                concurrency.Release();
            }
        });
        return [.. await Task.WhenAll(tasks)];
    }

    private async Task<StudentPerformance> LoadStudentAsync(string email)
    {
        var registrationTask = registrations.GetEntityIfExistsAsync<SubscriptionRegistration>(
            SubscriptionRegistration.Partition,
            SubscriptionRegistration.EmailRowKey(email));
        var stateTask = ReadPartitionAsync<GameState>(gameStates, email);
        var lockTask = gameStates.GetEntityIfExistsAsync<GameTaskLock>(
            email,
            GameTaskLock.LockRowKey);
        var passTask = ReadPartitionAsync<PassTestEntity>(passTests, email);
        var failTask = ReadPartitionAsync<FailTestEntity>(failTests, email);
        await Task.WhenAll(
            registrationTask,
            stateTask,
            lockTask,
            passTask,
            failTask);

        var registration = registrationTask.Result.HasValue
            ? registrationTask.Result.Value
            : null;
        var registered = false;
        string? subscriptionId = null;
        if (registration is not null &&
            registration.IndexKind == SubscriptionRegistration.EmailIndexKind &&
            registration.Email == email &&
            Guid.TryParse(registration.SubscriptionId, out var subscriptionGuid))
        {
            var normalizedSubscriptionId =
                SubscriptionRegistration.NormalizeSubscriptionId(subscriptionGuid);
            var subscriptionIndex =
                await registrations.GetEntityIfExistsAsync<SubscriptionRegistration>(
                    SubscriptionRegistration.Partition,
                    SubscriptionRegistration.SubscriptionRowKey(
                        normalizedSubscriptionId));
            if (subscriptionIndex.HasValue)
            {
                var index = subscriptionIndex.Value!;
                registered =
                    index.IndexKind ==
                        SubscriptionRegistration.SubscriptionIndexKind &&
                    index.Email == email &&
                    index.SubscriptionId == normalizedSubscriptionId;
            }
            subscriptionId = registered ? normalizedSubscriptionId : null;
        }
        var states = stateTask.Result;
        var passes = passTask.Result;
        var failures = failTask.Result;
        var activeLock = lockTask.Result.HasValue
            ? lockTask.Result.Value
            : null;
        var gameRows = states
            .Where(item => !item.RowKey.StartsWith(
                "__",
                StringComparison.Ordinal))
            .ToList();
        var lastActivity = gameRows
            .Select(item => new DateTimeOffset(
                DateTime.SpecifyKind(item.LastUpdated, DateTimeKind.Utc)))
            .Concat(passes.Select(item => item.PassedAt))
            .Concat(failures.Select(item => item.FailedAt))
            .DefaultIfEmpty()
            .Max();

        var taskAttempts = passes
            .Where(item => !string.IsNullOrWhiteSpace(item.TaskName))
            .Select(item => (item.TaskName, Passes: 1, Failures: 0))
            .Concat(failures
                .Where(item => !string.IsNullOrWhiteSpace(item.TaskName))
                .Select(item => (item.TaskName, Passes: 0, Failures: 1)))
            .GroupBy(item => item.TaskName, StringComparer.Ordinal)
            .Select(group => new TaskAttempt(
                email,
                group.Key,
                group.Sum(item => item.Passes),
                group.Sum(item => item.Failures)))
            .ToList();

        return new StudentPerformance(
            email,
            registered,
            subscriptionId,
            passes.Sum(item => item.Mark),
            passes.Select(item => item.TaskName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            failures.Count,
            activeLock is null
                ? null
                : new ActiveTask(activeLock.TaskName, activeLock.Npc, activeLock.Game),
            lastActivity == default ? null : lastActivity,
            passes,
            failures,
            taskAttempts);
    }

    private static object ToStudentResponse(StudentPerformance student) => new
    {
        student.Email,
        student.Registered,
        student.SubscriptionId,
        student.TotalMarks,
        student.CompletedTaskCount,
        student.FailedAttemptCount,
        student.ActiveTask,
        student.LastActivity
    };

    private static async Task<List<T>> ReadPartitionAsync<T>(
        TableClient table,
        string partitionKey)
        where T : class, ITableEntity, new()
    {
        var items = new List<T>();
        await foreach (var item in table.QueryAsync<T>(
                           entity => entity.PartitionKey == partitionKey))
        {
            items.Add(item);
        }

        return items;
    }

    private async Task<ClassDefinition?> GetOwnedClassAsync(
        string ownerEmail,
        string classId)
    {
        if (!Guid.TryParseExact(classId, "N", out _))
        {
            return null;
        }

        var response = await classes.GetEntityIfExistsAsync<ClassDefinition>(
            ClassDefinition.OwnerPartition(ownerEmail),
            classId);
        if (!response.HasValue)
        {
            return null;
        }

        var classEntity = response.Value!;
        return classEntity.OwnerEmail == ownerEmail ? classEntity : null;
    }

    private IActionResult? Authorize(HttpRequest request) =>
        authorizer.Authorize(request) switch
        {
            OperatorAuthorizationStatus.Authorized => null,
            OperatorAuthorizationStatus.Forbidden =>
                new StatusCodeResult(StatusCodes.Status403Forbidden),
            _ => new UnauthorizedObjectResult(
                ApiResponse.ErrorResult("Signed operator identity is required."))
        };

    private static bool TryNormalizeEmail(
        string value,
        out string normalizedEmail)
    {
        normalizedEmail = SubscriptionRegistration.NormalizeEmail(value);
        return IsExactEmail(normalizedEmail);
    }

    private static bool IsExactEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed) &&
        parsed.Address.Equals(value, StringComparison.OrdinalIgnoreCase);

    private static IActionResult Success(object data) =>
        new OkObjectResult(ApiResponse<object>.SuccessResult(data));

    private static IActionResult BadRequest(string message) =>
        new BadRequestObjectResult(ApiResponse.ErrorResult(message));

    private static IActionResult NotFound(string message) =>
        new NotFoundObjectResult(ApiResponse.ErrorResult(message));

    private sealed record StudentPerformance(
        string Email,
        bool Registered,
        string? SubscriptionId,
        int TotalMarks,
        int CompletedTaskCount,
        int FailedAttemptCount,
        ActiveTask? ActiveTask,
        DateTimeOffset? LastActivity,
        List<PassTestEntity> PassedTests,
        List<FailTestEntity> FailedTests,
        List<TaskAttempt> TaskAttempts);

    private sealed record ActiveTask(string TaskName, string Npc, string Game);
    private sealed record TaskAttempt(
        string Email,
        string TaskName,
        int Passes,
        int Failures);
}
