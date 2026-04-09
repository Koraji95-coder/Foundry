namespace Foundry.Models;

/// <summary>
/// Per-topic accuracy summary aggregated across all training attempts.
/// Used to identify weak areas and generate coaching recommendations.
/// </summary>
public sealed class TopicMasterySummary
{
    /// <summary>Name of the study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Total number of questions attempted for this topic across all sessions.</summary>
    public int Attempted { get; init; }

    /// <summary>Number of questions answered correctly across all sessions.</summary>
    public int Correct { get; init; }

    /// <summary>Fraction of questions answered correctly, in the range [0, 1].</summary>
    public double Accuracy => Attempted == 0 ? 0 : (double)Correct / Attempted;

    /// <summary>Compact one-line display string including topic name and accuracy fraction.</summary>
    public string DisplaySummary =>
        $"{Topic}: {Correct}/{Attempted} correct ({Accuracy:P0})";
}
