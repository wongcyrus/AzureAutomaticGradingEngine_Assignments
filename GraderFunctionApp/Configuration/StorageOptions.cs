using System.ComponentModel.DataAnnotations;

namespace GraderFunctionApp.Configuration
{
    public class StorageOptions
    {
        public const string SectionName = "Storage";

        public string TestResultsContainerName { get; set; } = "test-results";
        public string PassTestTableName { get; set; } = "PassTests";
        public string FailTestTableName { get; set; } = "FailTests";
        public string SubscriptionRegistrationsTableName { get; set; } = "SubscriptionRegistrations";
        public string ClassesTableName { get; set; } = "Classes";
        public string ClassMembershipsTableName { get; set; } = "ClassMemberships";
        public string GameStatesTableName { get; set; } = "GameStates";
        public string NPCCharacterTableName { get; set; } = "NPCCharacter";
        public string PreGeneratedMessageTableName { get; set; } = "PreGeneratedMessages";
    }
}
