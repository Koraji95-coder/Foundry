namespace Foundry.Models;

/// <summary>
/// Result produced by the TensorFlow forecasting engine (or its linear-regression fallback).
/// Contains per-topic accuracy forecasts, plateau detections, performance anomalies, and
/// mastery time-to-mastery estimates. Consumes PyTorch similarity scores via the EngineHandoff
/// contract and is the final output stage of the three-engine ML pipeline.
/// </summary>
public sealed class MLForecastResult
{
    /// <summary>Whether the forecasting engine ran successfully (false when falling back to linear regression).</summary>
    public bool Ok { get; init; }

    /// <summary>Which engine produced this result: "tensorflow", "linear", or "fallback".</summary>
    public string Engine { get; init; } = "linear";

    /// <summary>Per-topic accuracy forecasts for the next session and the next five sessions.</summary>
    public IReadOnlyList<MLTopicForecast> Forecasts { get; init; } = Array.Empty<MLTopicForecast>();

    /// <summary>Topics where accuracy has plateaued and no further improvement is predicted without intervention.</summary>
    public IReadOnlyList<MLPlateauDetection> Plateaus { get; init; } = Array.Empty<MLPlateauDetection>();

    /// <summary>Topics with detected performance anomalies (significant accuracy drops).</summary>
    public IReadOnlyList<MLAnomaly> Anomalies { get; init; } = Array.Empty<MLAnomaly>();

    /// <summary>Estimated sessions and days required for each topic to reach the mastery threshold.</summary>
    public IReadOnlyList<MLMasteryEstimate> MasteryEstimates { get; init; } = Array.Empty<MLMasteryEstimate>();

    /// <summary>Error message from the TensorFlow engine, if any.</summary>
    public string? TensorflowError { get; init; }
}

/// <summary>Per-topic accuracy forecast for upcoming study sessions.</summary>
public sealed class MLTopicForecast
{
    /// <summary>Name of the study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Most recent observed accuracy, in the range [0, 1].</summary>
    public double CurrentAccuracy { get; init; }

    /// <summary>Predicted accuracy for the immediately next session.</summary>
    public double PredictedNextSession { get; init; }

    /// <summary>Predicted accuracy after five additional sessions.</summary>
    public double Predicted5Sessions { get; init; }

    /// <summary>Named trend direction: "improving", "declining", or "stable".</summary>
    public string Trend { get; init; } = "stable";

    /// <summary>Slope of the accuracy trend line (sessions as the independent variable).</summary>
    public double TrendSlope { get; init; }

    /// <summary>Number of historical data points used to fit the forecast model.</summary>
    public int DataPoints { get; init; }

    /// <summary>Model confidence in the forecast, in the range [0, 1].</summary>
    public double Confidence { get; init; }
}

/// <summary>Plateau detection result for a topic where accuracy has stalled.</summary>
public sealed class MLPlateauDetection
{
    /// <summary>Name of the study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Accuracy level at which the plateau was detected.</summary>
    public double PlateauAccuracy { get; init; }

    /// <summary>Number of consecutive sessions since the plateau began.</summary>
    public int SessionsSincePlateau { get; init; }

    /// <summary>Suggested action to break the plateau (e.g. "Switch to challenge mode").</summary>
    public string Recommendation { get; init; } = string.Empty;
}

/// <summary>A detected accuracy anomaly — a significant drop from the recent average.</summary>
public sealed class MLAnomaly
{
    /// <summary>Name of the affected study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Rolling average accuracy before the anomaly.</summary>
    public double PreviousAverage { get; init; }

    /// <summary>Accuracy in the most recent session that triggered the anomaly.</summary>
    public double CurrentAccuracy { get; init; }

    /// <summary>Magnitude of the accuracy drop (PreviousAverage − CurrentAccuracy).</summary>
    public double Drop { get; init; }

    /// <summary>Severity label: "mild", "moderate", or "severe".</summary>
    public string Severity { get; init; } = "moderate";
}

/// <summary>Estimate of the number of sessions and days needed to reach the mastery threshold.</summary>
public sealed class MLMasteryEstimate
{
    /// <summary>Name of the study topic.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Current accuracy, in the range [0, 1].</summary>
    public double CurrentAccuracy { get; init; }

    /// <summary>Target accuracy threshold that defines mastery (default 0.9).</summary>
    public double TargetAccuracy { get; init; } = 0.9;

    /// <summary>Estimated number of additional practice sessions needed to reach the target.</summary>
    public int EstimatedSessions { get; init; }

    /// <summary>Estimated number of calendar days needed to reach the target at the current pace.</summary>
    public int EstimatedDays { get; init; }

    /// <summary>Model confidence in the estimate, in the range [0, 1].</summary>
    public double Confidence { get; init; }

    /// <summary>Whether the topic has already reached the mastery threshold.</summary>
    public bool? Mastered { get; init; }
}
