namespace Foundry.Models;

/// <summary>
/// Point-in-time snapshot of job system metrics returned by <c>GET /api/jobs/metrics</c>.
/// </summary>
public sealed class FoundryJobMetrics
{
    /// <summary>Total number of jobs in the store across all statuses.</summary>
    public int TotalJobs { get; set; }

    /// <summary>Number of jobs currently waiting to be processed.</summary>
    public int QueuedCount { get; set; }

    /// <summary>Number of jobs that are currently being processed by the job worker.</summary>
    public int RunningCount { get; set; }

    /// <summary>Number of jobs that completed successfully.</summary>
    public int SucceededCount { get; set; }

    /// <summary>Number of jobs that failed or were recovered after a broker crash.</summary>
    public int FailedCount { get; set; }

    /// <summary>Average wall-clock duration in seconds across all succeeded jobs, or null if none.</summary>
    public double? AverageDurationSeconds { get; set; }

    /// <summary>Number of jobs that completed (succeeded or failed) within the last hour.</summary>
    public int CompletedLastHour { get; set; }

    /// <summary>Number of jobs that completed (succeeded or failed) within the last 24 hours.</summary>
    public int CompletedLastDay { get; set; }
}
