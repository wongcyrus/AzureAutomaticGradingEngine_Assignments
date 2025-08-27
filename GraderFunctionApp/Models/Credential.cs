using Azure;
using Microsoft.WindowsAzure.Storage.Table;
using ITableEntity = Azure.Data.Tables.ITableEntity;
namespace GraderFunctionApp.Models;

public class Credential : ITableEntity
{
    public string? AppId { get; set; }
    public string? DisplayName { get; set; }
    public string? Password { get; set; }
    public string? Tenant { get; set; }

    public string? SubscriptionId { get; set; }
    public string? PartitionKey { get; set; }
    public string? RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

}