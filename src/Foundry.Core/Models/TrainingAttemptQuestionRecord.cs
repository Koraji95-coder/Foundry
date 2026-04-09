namespace Foundry.Models;

/// <summary>
/// Correctness record for a single question within a <see cref="TrainingAttemptRecord"/>.
/// </summary>
public sealed class TrainingAttemptQuestionRecord
{
    /// <summary>The study topic this question belongs to.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Difficulty level of the question (e.g. "easy", "medium", "hard").</summary>
    public string Difficulty { get; init; } = string.Empty;

    /// <summary>Whether the operator answered this question correctly.</summary>
    public bool Correct { get; init; }
}
