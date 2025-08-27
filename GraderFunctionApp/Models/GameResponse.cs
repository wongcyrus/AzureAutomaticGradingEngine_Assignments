using System.Runtime.Serialization;

namespace GraderFunctionApp.Models
{
    [DataContract]
    public class GameResponse
    {
        [DataMember] public string Status { get; set; } = "OK";
        [DataMember] public string Message { get; set; } = string.Empty;
        [DataMember] public string NextGamePhrase { get; set; } = string.Empty;
        [DataMember] public string ReportUrl { get; set; } = string.Empty;
        [DataMember] public string EasterEggUrl { get; set; } = string.Empty;
        [DataMember] public int Score { get; set; } = 0;
        [DataMember] public int CompletedTasks { get; set; } = 0;
        [DataMember] public bool TaskCompleted { get; set; } = false;
        [DataMember] public string TaskName { get; set; } = string.Empty;
        [DataMember] public Dictionary<string, object> AdditionalData { get; set; } = new();

        public static GameResponse Success(string message, string nextPhase = "", string reportUrl = "", string easterEggUrl = "")
        {
            return new GameResponse
            {
                Status = "OK",
                Message = message,
                NextGamePhrase = nextPhase,
                ReportUrl = reportUrl,
                EasterEggUrl = easterEggUrl
            };
        }

        public static GameResponse Error(string message, string status = "ERROR")
        {
            return new GameResponse
            {
                Status = status,
                Message = message
            };
        }
    }
}
