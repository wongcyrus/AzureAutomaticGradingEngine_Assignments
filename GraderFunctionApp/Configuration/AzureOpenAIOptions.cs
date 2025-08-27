using System.ComponentModel.DataAnnotations;

namespace GraderFunctionApp.Configuration
{
    public class AzureOpenAIOptions
    {
        public const string SectionName = "AzureOpenAI";

        [Required]
        public string Endpoint { get; set; } = string.Empty;

        [Required]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        public string DeploymentName { get; set; } = string.Empty;
    }
}
