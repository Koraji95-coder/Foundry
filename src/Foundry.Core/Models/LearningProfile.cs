namespace Foundry.Models;

/// <summary>
/// Summarises the operator's current learning state: what topics they are working on,
/// what they need most right now, and any active coaching rules.
/// Built by <see cref="Foundry.Services.FoundryOrchestrator"/> from training history and ML results.
/// </summary>
public sealed class LearningProfile
{
    /// <summary>One-paragraph narrative of the operator's current learning status.</summary>
    public string Summary { get; init; } = "No learning profile yet.";

    /// <summary>
    /// The single most important action the operator should take now
    /// (e.g. "Review networking topics before tomorrow's exam").
    /// </summary>
    public string CurrentNeed { get; init; } =
        "Add a few knowledge files and score a practice test to personalize the desk.";

    /// <summary>Topics the operator is actively studying or that are currently due for review.</summary>
    public IReadOnlyList<string> ActiveTopics { get; init; } = Array.Empty<string>();

    /// <summary>Active coaching rules derived from the operator's performance patterns.</summary>
    public IReadOnlyList<string> CoachingRules { get; init; } = Array.Empty<string>();
}
