namespace GraderFunctionApp.Models
{
    public class RequestBodyModel
    {
        public required string Trace { get; set; }
        public required string Credentials { get; set; }
        public required string Filter { get; set; }
    }
}
