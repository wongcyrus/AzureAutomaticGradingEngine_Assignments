namespace GraderFunctionApp.Models
{
    public class RequestBodyModel
    {
        public required string Trace { get; set; }
        public required string SubscriptionId { get; set; }
        public required string Filter { get; set; }
    }
}
