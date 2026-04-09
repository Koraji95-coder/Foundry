namespace Foundry.Models;

/// <summary>
/// Persisted record of a single practice test attempt, including per-question detail.
/// Stored in the LiteDB <c>training_practice_attempts</c> collection.
/// </summary>
public sealed class TrainingAttemptRecord
{
    /// <summary>Short title for the practice session (e.g. the question set name).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The study topic or domain focus of the session.</summary>
    public string Focus { get; init; } = string.Empty;

    /// <summary>Difficulty level (e.g. "easy", "medium", "hard").</summary>
    public string Difficulty { get; init; } = string.Empty;

    /// <summary>How the questions were generated (e.g. "ollama", "manual").</summary>
    public string GenerationSource { get; init; } = string.Empty;

    /// <summary>When the practice session was completed.</summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>Total number of questions in the session.</summary>
    public int QuestionCount { get; init; }

    /// <summary>Number of questions answered correctly.</summary>
    public int CorrectCount { get; init; }

    /// <summary>Per-question topic and correctness records.</summary>
    public IReadOnlyList<TrainingAttemptQuestionRecord> Questions { get; init; } = Array.Empty<TrainingAttemptQuestionRecord>();

    /// <summary>Fraction of questions answered correctly, in the range [0, 1].</summary>
    public double Accuracy => QuestionCount == 0 ? 0 : (double)CorrectCount / QuestionCount;

    /// <summary>Compact one-line display string for logs or the Discord bot.</summary>
    public string DisplaySummary =>
        $"{CompletedAt:yyyy-MM-dd HH:mm} | {CorrectCount}/{QuestionCount} correct | {Difficulty} | {Focus}";
}
