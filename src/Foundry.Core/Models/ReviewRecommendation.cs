namespace Foundry.Models;

/// <summary>
/// A scheduled review recommendation for a single study topic, generated from
/// spaced-repetition logic applied to the operator's training history.
/// </summary>
public sealed class ReviewRecommendation
{
    /// <summary>Name of the study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Total number of questions attempted for this topic.</summary>
    public int Attempted { get; init; }

    /// <summary>Number of questions answered correctly.</summary>
    public int Correct { get; init; }

    /// <summary>When the operator last practiced this topic.</summary>
    public DateTimeOffset LastPracticedAt { get; init; }

    /// <summary>Scheduled date/time when this topic is next due for review.</summary>
    public DateTimeOffset DueAt { get; init; }

    /// <summary>Priority label (e.g. "high", "medium", "low").</summary>
    public string Priority { get; init; } = string.Empty;

    /// <summary>Human-readable explanation for why this topic is recommended for review.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Fraction of questions answered correctly, in the range [0, 1].</summary>
    public double Accuracy => Attempted == 0 ? 0 : (double)Correct / Attempted;

    /// <summary>Whether the review is currently due (i.e. <see cref="DueAt"/> is in the past).</summary>
    public bool IsDue => DueAt <= DateTimeOffset.Now;

    /// <summary>Compact one-line display string including topic, priority, due date, and accuracy.</summary>
    public string DisplaySummary =>
        $"{Topic} | {Priority} | due {DueAt:yyyy-MM-dd} | {Correct}/{Attempted} correct ({Accuracy:P0})";
}
