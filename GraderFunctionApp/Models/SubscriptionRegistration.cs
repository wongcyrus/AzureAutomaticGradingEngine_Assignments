using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;

public class SubscriptionRegistration : ITableEntity
{
    public const string Partition = "registrations";
    public const string EmailIndexKind = "email";
    public const string SubscriptionIndexKind = "subscription";

    public string Email { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string IndexKind { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    public static string NormalizeSubscriptionId(Guid subscriptionId) =>
        subscriptionId.ToString("D").ToLowerInvariant();

    public static string EmailRowKey(string normalizedEmail)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return $"email:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string SubscriptionRowKey(string normalizedSubscriptionId) =>
        $"subscription:{normalizedSubscriptionId}";

    public static SubscriptionRegistration CreateEmailIndex(
        string normalizedEmail,
        string normalizedSubscriptionId) =>
        new()
        {
            RowKey = EmailRowKey(normalizedEmail),
            Email = normalizedEmail,
            SubscriptionId = normalizedSubscriptionId,
            IndexKind = EmailIndexKind
        };

    public static SubscriptionRegistration CreateSubscriptionIndex(
        string normalizedEmail,
        string normalizedSubscriptionId) =>
        new()
        {
            RowKey = SubscriptionRowKey(normalizedSubscriptionId),
            Email = normalizedEmail,
            SubscriptionId = normalizedSubscriptionId,
            IndexKind = SubscriptionIndexKind
        };
}
