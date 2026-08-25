using Azure;
using Azure.Data.Tables;
using GraderFunctionApp.Models;

const string tableName = "SubscriptionRegistrations";

if (args.Length is < 1 or > 2 ||
    (args.Length == 2 && args[1] != "--yes"))
{
    Console.Error.WriteLine(
        "Usage: StudentSubscriptionAdmin <student-email> [--yes]");
    return 2;
}

var connectionString =
    Environment.GetEnvironmentVariable("GRADING_STORAGE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Error: grading storage credentials are unavailable.");
    return 1;
}

var email = SubscriptionRegistration.NormalizeEmail(args[0]);
var emailRowKey = SubscriptionRegistration.EmailRowKey(email);
var table = new TableClient(connectionString, tableName);
var deletionStarted = false;

try
{
    var emailResponse =
        await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
            SubscriptionRegistration.Partition,
            emailRowKey);
    if (!emailResponse.HasValue)
    {
        Console.Error.WriteLine(
            "Error: no subscription registration exists for that student.");
        return 1;
    }

    var emailIndex = emailResponse.Value!;
    if (!IsValidEmailIndex(emailIndex, email, emailRowKey))
    {
        Console.Error.WriteLine(
            "Error: the subscription registration indexes are inconsistent.");
        return 1;
    }

    var subscriptionRowKey =
        SubscriptionRegistration.SubscriptionRowKey(emailIndex.SubscriptionId);
    var subscriptionResponse =
        await table.GetEntityIfExistsAsync<SubscriptionRegistration>(
            SubscriptionRegistration.Partition,
            subscriptionRowKey);
    if (!subscriptionResponse.HasValue)
    {
        Console.Error.WriteLine(
            "Error: the subscription registration indexes are incomplete.");
        return 1;
    }

    var subscriptionIndex = subscriptionResponse.Value!;
    if (!IsValidSubscriptionIndex(
            subscriptionIndex,
            email,
            emailIndex.SubscriptionId,
            subscriptionRowKey))
    {
        Console.Error.WriteLine(
            "Error: the subscription registration indexes are inconsistent.");
        return 1;
    }

    Console.WriteLine("The following registration will be removed:");
    Console.WriteLine($"  Student email:   {email}");
    Console.WriteLine($"  Subscription ID: {emailIndex.SubscriptionId}");
    Console.WriteLine($"  Table:           {tableName}");
    Console.WriteLine(
        "Azure access, tags, game progress, reports, and test results will not be changed.");

    var assumeYes = args.Length == 2;
    if (!assumeYes)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "Error: confirmation requires an interactive terminal; use --yes.");
            return 1;
        }

        Console.Write("Release this subscription registration? [y/N] ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Release cancelled.");
            return 0;
        }
    }

    deletionStarted = true;
    await table.SubmitTransactionAsync(
    [
        new TableTransactionAction(
            TableTransactionActionType.Delete,
            emailIndex,
            emailIndex.ETag),
        new TableTransactionAction(
            TableTransactionActionType.Delete,
            subscriptionIndex,
            subscriptionIndex.ETag)
    ]);
    Console.WriteLine($"Released the subscription registration for {email}.");
    return 0;
}
catch (RequestFailedException ex) when (ex.Status == 404 && !deletionStarted)
{
    Console.Error.WriteLine(
        "Error: no subscription registration exists for that student.");
    return 1;
}
catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412)
{
    Console.Error.WriteLine(
        "Error: the registration changed concurrently and was not removed.");
    return 1;
}
catch (RequestFailedException ex)
{
    Console.Error.WriteLine(
        $"Error: Azure Table Storage request failed with status {ex.Status}.");
    return 1;
}

static bool IsValidEmailIndex(
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

static bool IsValidSubscriptionIndex(
    SubscriptionRegistration index,
    string email,
    string subscriptionId,
    string rowKey) =>
    index.PartitionKey == SubscriptionRegistration.Partition &&
    index.RowKey == rowKey &&
    index.IndexKind == SubscriptionRegistration.SubscriptionIndexKind &&
    index.Email == email &&
    index.SubscriptionId == subscriptionId;
