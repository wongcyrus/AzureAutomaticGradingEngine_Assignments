namespace GraderFunctionApp.Interfaces
{
    public interface IAIService
    {
        Task<string?> RephraseInstructionAsync(string instruction);
    }
}
