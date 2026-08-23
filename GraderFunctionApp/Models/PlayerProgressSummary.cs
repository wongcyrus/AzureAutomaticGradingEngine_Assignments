namespace GraderFunctionApp.Models;

public sealed class PlayerProgressSummary
{
    public string Email { get; init; } = string.Empty;
    public string? SubscriptionId { get; init; }
    public int TotalMarks { get; init; }
    public IReadOnlyList<PassedTaskSummary> PassedTasks { get; init; } = [];
    public int FailedAttemptCount { get; init; }
    public IReadOnlyList<FailedAttemptSummary> FailedAttempts { get; init; } = [];
    public ActiveTaskSummary? ActiveTask { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
}

public sealed class PassedTaskSummary
{
    public string Name { get; init; } = string.Empty;
    public int Mark { get; init; }
}

public sealed class FailedAttemptSummary
{
    public string TestName { get; init; } = string.Empty;
    public string TaskName { get; init; } = string.Empty;
    public string AssignedByNpc { get; init; } = string.Empty;
    public DateTimeOffset FailedAt { get; init; }
}

public sealed class ActiveTaskSummary
{
    public string Name { get; init; } = string.Empty;
    public string Npc { get; init; } = string.Empty;
    public int Reward { get; init; }
}

public sealed class ResetGameResult
{
    public string Email { get; init; } = string.Empty;
    public int RemovedGameStates { get; init; }
    public int RemovedPassedTests { get; init; }
    public int PreservedFailedAttempts { get; init; }
}
