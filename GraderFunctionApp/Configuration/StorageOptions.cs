using System.ComponentModel.DataAnnotations;

namespace GraderFunctionApp.Configuration
{
    public class StorageOptions
    {
        public const string SectionName = "Storage";

        public string TestResultsContainerName { get; set; } = "test-results";
        public string PassTestTableName { get; set; } = "PassTests";
        public string FailTestTableName { get; set; } = "FailTests";
        public string SubscriptionTableName { get; set; } = "Subscription";
        public string CredentialTableName { get; set; } = "Credential";
        public string NPCCharacterTableName { get; set; } = "NPCCharacter";
        public string PreGeneratedMessageTableName { get; set; } = "PreGeneratedMessages";
    }
}
