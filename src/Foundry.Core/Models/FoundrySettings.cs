using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Models;

/// <summary>
/// Immutable settings loaded from <c>foundry.settings.json</c> (and the optional
/// <c>foundry.settings.local.json</c> override) at startup.
/// Local settings are merged on top of the base file so individual machines can override
/// paths without modifying the committed settings file.
/// </summary>
public sealed class FoundrySettings
{
    /// <summary>Absolute path to the Suite repository on this machine (default: ~/Documents/GitHub/Suite).</summary>
    public string SuiteRepoPath { get; init; } = GetDefaultSuiteRepoPath();

    /// <summary>HTTP endpoint of the Suite runtime status API (default: http://127.0.0.1:5000/api/runtime/status).</summary>
    public string SuiteRuntimeStatusEndpoint { get; init; } =
        "http://127.0.0.1:5000/api/runtime/status";

    /// <summary>Base URL of the Ollama API (default: http://127.0.0.1:11434).</summary>
    public string OllamaEndpoint { get; init; } = "http://127.0.0.1:11434";

    /// <summary>LLM model name used for ML pipeline operations (default: "qwen3:8b").</summary>
    public string MLModel { get; init; } = "qwen3:8b";

    /// <summary>Whether the three-engine ML pipeline (analytics/embeddings/forecast) is active.</summary>
    public bool EnableMLPipeline { get; init; }

    /// <summary>
    /// Override path for ML artifact exports. When empty, artifacts are written to
    /// <c>&lt;StateRootPath&gt;/ml-artifacts/</c>.
    /// </summary>
    public string MLArtifactExportPath { get; init; } = string.Empty;

    /// <summary>How many days to retain completed job records before they are purged (default: 30).</summary>
    public int JobRetentionDays { get; init; } = 30;

    /// <summary>Override path for the primary knowledge library directory.</summary>
    public string KnowledgeLibraryPath { get; init; } = string.Empty;

    /// <summary>Override path for the Foundry state root directory (equivalent to <c>FOUNDRY_STATE_ROOT</c>).</summary>
    public string StateRootPath { get; init; } = string.Empty;

    /// <summary>Additional knowledge source directories scanned alongside <see cref="KnowledgeLibraryPath"/>.</summary>
    public IReadOnlyList<string> AdditionalKnowledgePaths { get; init; } = Array.Empty<string>();

    /// <summary>Discord bot token (optional; loaded from settings or environment).</summary>
    public string? DiscordBotToken { get; init; }

    /// <summary>
    /// Resolves the effective ML artifact export path.
    /// Returns the absolute path of <see cref="MLArtifactExportPath"/> when set,
    /// otherwise <c>&lt;StateRootPath&gt;/ml-artifacts/</c>.
    /// </summary>
    /// <param name="baseDirectory">Foundry repo root, used to resolve relative paths.</param>
    public string ResolveMLArtifactExportPath(string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(MLArtifactExportPath))
        {
            return Path.GetFullPath(MLArtifactExportPath);
        }

        return Path.Combine(ResolveStateRootPath(baseDirectory), "ml-artifacts");
    }

    /// <summary>
    /// Resolves the effective knowledge library path.
    /// Returns the absolute path of <see cref="KnowledgeLibraryPath"/> when set,
    /// otherwise the default path under the state root.
    /// </summary>
    /// <param name="baseDirectory">Foundry repo root, used to resolve relative paths.</param>
    public string ResolveKnowledgeLibraryPath(string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(KnowledgeLibraryPath))
        {
            return Path.GetFullPath(KnowledgeLibraryPath);
        }

        return GetDefaultKnowledgeLibraryPath();
    }

    /// <summary>
    /// Resolves the effective state root path.
    /// Priority order: <see cref="StateRootPath"/> setting → <c>FOUNDRY_STATE_ROOT</c> env var
    /// → platform default (<c>C:\FoundryState</c> on Windows, <c>~/foundry-state</c> elsewhere).
    /// </summary>
    /// <param name="baseDirectory">Foundry repo root, used to resolve relative paths.</param>
    public string ResolveStateRootPath(string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(StateRootPath))
        {
            return Path.GetFullPath(StateRootPath);
        }

        return GetDefaultStateRootPath();
    }

    /// <summary>
    /// Returns a deduplicated, trimmed list of non-empty additional knowledge paths.
    /// </summary>
    public IReadOnlyList<string> ResolveAdditionalKnowledgePaths()
    {
        return AdditionalKnowledgePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Loads settings from <c>foundry.settings.json</c> and, if present,
    /// merges <c>foundry.settings.local.json</c> on top.
    /// Returns a default <see cref="FoundrySettings"/> instance if neither file exists or parsing fails.
    /// </summary>
    /// <param name="baseDirectory">Directory that contains the settings files (typically the Foundry repo root).</param>
    public static FoundrySettings Load(string baseDirectory)
    {
        var settingsPath = Path.Combine(baseDirectory, "foundry.settings.json");
        var localSettingsPath = Path.Combine(baseDirectory, "foundry.settings.local.json");
        if (!File.Exists(settingsPath) && !File.Exists(localSettingsPath))
        {
            return new FoundrySettings();
        }

        try
        {
            var rootNode = new JsonObject();
            MergeSettingsFile(rootNode, settingsPath);
            MergeSettingsFile(rootNode, localSettingsPath);
            return rootNode.Deserialize<FoundrySettings>(
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                   )
                   ?? new FoundrySettings();
        }
        catch
        {
            return new FoundrySettings();
        }
    }

    private static string GetDefaultSuiteRepoPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "Documents", "GitHub", "Suite");
        }

        return Path.Combine("C:\\Users\\Public", "Documents", "GitHub", "Suite");
    }

    private static string GetDefaultKnowledgeLibraryPath()
    {
        var stateRoot = GetDefaultStateRootPath();
        return Path.Combine(stateRoot, "knowledge");
    }

    private static string GetDefaultStateRootPath()
    {
        var envVal = Environment.GetEnvironmentVariable("FOUNDRY_STATE_ROOT");
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        if (OperatingSystem.IsWindows())
        {
            return @"C:\FoundryState";
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? Path.Combine("/tmp", "foundry-state")
            : Path.Combine(home, "foundry-state");
    }

    private static void MergeSettingsFile(JsonObject target, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var payload = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var parsed = JsonNode.Parse(payload) as JsonObject;
        if (parsed is null)
        {
            return;
        }

        foreach (var property in parsed)
        {
            target[property.Key] = property.Value?.DeepClone();
        }
    }
}
