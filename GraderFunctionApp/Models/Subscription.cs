
using Azure;
using Azure.Data.Tables;

namespace GraderFunctionApp.Models;


internal class Subscription : ITableEntity
{
    public const string RegistrationRowKey = "registration";

    public string? SubscriptionId { get; set; }
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}