using Foundry.Services;

namespace Foundry.Models;

/// <summary>
/// Aggregate broker state snapshot returned by <c>GET /api/status</c>.
/// Combines broker metadata, provider status, and ML pipeline results into a single payload.
/// </summary>
public sealed class FoundryBrokerState
{
    /// <summary>When this snapshot was generated.</summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Broker host, port, and liveness information.</summary>
    public FoundryBrokerStatusSection Broker { get; init; } = new();

    /// <summary>Active model provider and available model list.</summary>
    public FoundryProviderSection Provider { get; init; } = new();

    /// <summary>ML pipeline enable state and latest result summaries.</summary>
    public FoundryMLSection ML { get; init; } = new();
}

/// <summary>Broker host and liveness metadata within <see cref="FoundryBrokerState"/>.</summary>
public sealed class FoundryBrokerStatusSection
{
    /// <summary>Overall broker status (typically "ok").</summary>
    public string Status { get; init; } = "ok";

    /// <summary>Hostname or IP address the broker is bound to.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>TCP port the broker is listening on.</summary>
    public int Port { get; init; }

    /// <summary>Full base URL of the broker (e.g. "http://127.0.0.1:5000").</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Whether the broker is bound to loopback only (true) or a network interface (false).</summary>
    public bool LoopbackOnly { get; init; } = true;

    /// <summary>When the broker process started.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>When the broker last refreshed its state cache.</summary>
    public DateTimeOffset LastRefreshAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>Active model provider details within <see cref="FoundryBrokerState"/>.</summary>
public sealed class FoundryProviderSection
{
    /// <summary>Provider ID of the currently active model provider.</summary>
    public string ActiveProviderId { get; init; } = OllamaService.OllamaProviderId;

    /// <summary>Human-readable label of the currently active model provider.</summary>
    public string ActiveProviderLabel { get; init; } = OllamaService.OllamaProviderLabel;

    /// <summary>Human-readable label of the primary (default) provider.</summary>
    public string PrimaryProviderLabel { get; init; } = OllamaService.OllamaProviderLabel;

    /// <summary>Provider ID from the settings file (may differ from active if the configured provider is offline).</summary>
    public string ConfiguredProviderId { get; init; } = OllamaService.OllamaProviderId;

    /// <summary>Whether the active provider responded to a ping successfully.</summary>
    public bool Ready { get; init; }

    /// <summary>Number of models currently installed in the active provider.</summary>
    public int InstalledModelCount { get; init; }

    /// <summary>Names of all models currently installed in the active provider.</summary>
    public IReadOnlyList<string> InstalledModels { get; init; } = Array.Empty<string>();
}

/// <summary>ML pipeline state within <see cref="FoundryBrokerState"/>.</summary>
public sealed class FoundryMLSection
{
    /// <summary>Whether the ML pipeline is enabled in <c>foundry.settings.json</c>.</summary>
    public bool Enabled { get; init; }

    /// <summary>Human-readable summary of the ML pipeline state.</summary>
    public string Summary { get; init; } = "ML pipeline is not enabled. Set enableMLPipeline to true in settings.";

    /// <summary>Latest analytics result, or null if no run has completed.</summary>
    public MLAnalyticsResult? Analytics { get; init; }

    /// <summary>Latest forecast result, or null if no run has completed.</summary>
    public MLForecastResult? Forecast { get; init; }

    /// <summary>Latest embeddings result, or null if no run has completed.</summary>
    public MLEmbeddingsResult? Embeddings { get; init; }

    /// <summary>File path of the most recently exported artifact bundle, or null.</summary>
    public string? LastArtifactExportPath { get; init; }

    /// <summary>When the ML pipeline last completed a full run, or null.</summary>
    public DateTimeOffset? LastRunAt { get; init; }
}

/// <summary>
/// Result of a knowledge library import operation initiated via the broker API.
/// </summary>
public sealed class FoundryLibraryImportResult
{
    /// <summary>Number of documents successfully imported or re-indexed.</summary>
    public int ImportedCount { get; init; }

    /// <summary>File paths of successfully imported documents.</summary>
    public IReadOnlyList<string> ImportedPaths { get; init; } = Array.Empty<string>();

    /// <summary>File paths that were skipped (e.g. already up-to-date or unsupported format).</summary>
    public IReadOnlyList<string> SkippedPaths { get; init; } = Array.Empty<string>();
}
