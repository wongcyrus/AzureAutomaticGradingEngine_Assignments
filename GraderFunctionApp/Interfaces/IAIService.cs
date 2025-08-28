namespace GraderFunctionApp.Interfaces
{
    public interface IAIService
    {
        Task<string?> RephraseInstructionAsync(string instruction);
        Task<string?> PersonalizeNPCMessageAsync(string message, int age, string gender, string background);
    }
}
