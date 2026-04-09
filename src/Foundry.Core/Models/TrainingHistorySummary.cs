namespace Foundry.Models;

/// <summary>
/// Aggregate view of training history across all practice sessions.
/// Includes overall statistics, weak-topic breakdown, recent attempts, and review recommendations.
/// Built by <see cref="Foundry.Services.FoundryOrchestrator"/> from the LiteDB training collection.
/// </summary>
public sealed class TrainingHistorySummary
{
    /// <summary>Total number of practice sessions recorded.</summary>
    public int TotalAttempts { get; init; }

    /// <summary>Total questions attempted across all sessions.</summary>
    public int TotalQuestions { get; init; }

    /// <summary>Total questions answered correctly across all sessions.</summary>
    public int CorrectAnswers { get; init; }

    /// <summary>Topics where accuracy is below average, ordered by lowest accuracy first.</summary>
    public IReadOnlyList<TopicMasterySummary> WeakTopics { get; init; } = Array.Empty<TopicMasterySummary>();

    /// <summary>The most recent practice sessions, newest first.</summary>
    public IReadOnlyList<TrainingAttemptRecord> RecentAttempts { get; init; } = Array.Empty<TrainingAttemptRecord>();

    /// <summary>Spaced-repetition review recommendations ordered by due date.</summary>
    public IReadOnlyList<ReviewRecommendation> ReviewRecommendations { get; init; } = Array.Empty<ReviewRecommendation>();

    /// <summary>
    /// Human-readable one-line summary of overall accuracy across all recorded attempts.
    /// Returns a placeholder message when no history exists.
    /// </summary>
    public string OverallSummary
    {
        get
        {
            if (TotalAttempts == 0 || TotalQuestions == 0)
            {
                return "No scored practice history yet.";
            }

            var accuracy = (double)CorrectAnswers / TotalQuestions;
            return $"{TotalAttempts} attempts, {CorrectAnswers}/{TotalQuestions} correct overall ({accuracy:P0}).";
        }
    }

    /// <summary>
    /// Human-readable summary of the review queue: how many topics are due now vs. soon.
    /// Returns a placeholder message when no review targets have been scheduled yet.
    /// </summary>
    public string ReviewQueueSummary
    {
        get
        {
            if (ReviewRecommendations.Count == 0)
            {
                return "No review queue yet. Score a practice set to schedule follow-up work.";
            }

            var dueNow = ReviewRecommendations.Count(item => item.IsDue);
            var soon = ReviewRecommendations.Count(item => !item.IsDue && item.DueAt <= DateTimeOffset.Now.AddDays(2));
            return $"{dueNow} due now, {soon} due soon, {ReviewRecommendations.Count} tracked review targets.";
        }
    }
}
