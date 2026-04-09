using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Foundry.Services;

/// <summary>
/// Runs document embedding and ML artifact generation by delegating to the three-engine pipeline:
/// ONNX (first), Python/PyTorch (second), or a TF-IDF heuristic fallback (third).
/// Also handles export of <see cref="SuiteMLArtifactBundle"/> JSON files to the state directory.
/// Caches the most recent embeddings result for <see cref="DefaultCacheTtl"/> to avoid redundant inference.
/// </summary>
public sealed class MLAnalyticsService
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    private readonly ProcessRunner _processRunner;
    private readonly string _scriptsDirectory;
    private readonly OnnxMLEngine? _onnxEngine;
    private readonly TimeSpan _cacheTtl;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<MLAnalyticsService> _logger;
    private readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    // Python environment check (lazily evaluated, cached)
    private bool? _pythonAvailable;
    private readonly object _pythonCheckLock = new();

    // Result cache
    private MLEmbeddingsResult? _cachedEmbeddings;
    private DateTimeOffset _embeddingsCachedAt;

    /// <summary>
    /// Creates an <see cref="MLAnalyticsService"/> instance.
    /// </summary>
    /// <param name="processRunner">Used to invoke Python ML scripts as child processes.</param>
    /// <param name="scriptsDirectory">Absolute path to the directory containing ML Python scripts.</param>
    /// <param name="onnxEngine">Optional ONNX engine; when non-null it is tried before Python.</param>
    /// <param name="cacheTtl">How long to cache embeddings results. Defaults to 5 minutes.</param>
    /// <param name="resiliencePipeline">Optional Polly pipeline for Python script retries. Defaults to no-op.</param>
    /// <param name="logger">Optional logger. Defaults to a no-op logger when null.</param>
    public MLAnalyticsService(
        ProcessRunner processRunner,
        string scriptsDirectory,
        OnnxMLEngine? onnxEngine = null,
        TimeSpan? cacheTtl = null,
        ResiliencePipeline? resiliencePipeline = null,
        ILogger<MLAnalyticsService>? logger = null)
    {
        _processRunner = processRunner;
        _scriptsDirectory = scriptsDirectory;
        _onnxEngine = onnxEngine;
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
        _resiliencePipeline = resiliencePipeline ?? ResiliencePipeline.Empty;
        _logger = logger ?? NullLogger<MLAnalyticsService>.Instance;
    }

    /// <summary>
    /// Reports the current engine availability in priority order: onnx > python > fallback.
    /// </summary>
    public string ResolveAvailableEngine()
    {
        if (_onnxEngine?.IsAnalyticsModelAvailable == true)
            return "onnx";
        // NOTE: Python pytorch embedding path uses untrained random weights.
        // Production embeddings go through EmbeddingService (Ollama) → VectorStoreService (Qdrant).
        // Keep Python path as experimental fallback only — do not treat its output as semantic.
        if (IsPythonAvailable())
            return "python";
        return "fallback";
    }

    /// <summary>
    /// Runs document embeddings for the given documents using the best available engine
    /// (ONNX → Python → TF-IDF fallback). Results are cached for <see cref="DefaultCacheTtl"/>.
    /// </summary>
    /// <param name="documents">Documents to embed.</param>
    /// <param name="query">Optional query string for relevance ranking within the embeddings result.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<MLEmbeddingsResult> RunDocumentEmbeddingsAsync(
        IReadOnlyList<LearningDocument> documents,
        string? query = null,
        CancellationToken cancellationToken = default
    )
    {
        // Check cache
        if (_cachedEmbeddings is not null && DateTimeOffset.Now - _embeddingsCachedAt < _cacheTtl)
        {
            return _cachedEmbeddings;
        }

        var input = new
        {
            documents = documents.Select(d => new
            {
                id = d.FullPath,
                title = d.FileName,
                text = d.ExtractedText?.Length > 5000
                    ? d.ExtractedText[..5000]
                    : d.ExtractedText ?? string.Empty,
            }).ToArray(),
            query,
        };

        // 1. Try ONNX
        if (_onnxEngine is not null)
        {
            var onnxResult = _onnxEngine.RunEmbeddings(documents, query);
            if (onnxResult is not null)
            {
                CacheEmbeddings(onnxResult);
                return onnxResult;
            }
        }

        // 2. Try Python
        if (IsPythonAvailable())
        {
            try
            {
                var result = await RunPythonScriptAsync<MLEmbeddingsResult>(
                    "ml_document_embeddings.py",
                    input,
                    cancellationToken
                );

                if (result is not null)
                {
                    CacheEmbeddings(result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                return new MLEmbeddingsResult
                {
                    Ok = false,
                    Engine = "fallback",
                    PytorchError = ex.Message,
                };
            }
        }

        // 3. Fallback
        var fallbackResult = BuildFallbackEmbeddings();
        CacheEmbeddings(fallbackResult);
        return fallbackResult;
    }

    /// <summary>
    /// Generates a <see cref="SuiteMLArtifactBundle"/> from the combined ML results
    /// using the Python <c>ml_suite_artifacts.py</c> script, or a fallback bundle on failure.
    /// </summary>
    /// <param name="analytics">Analytics result to include in the bundle.</param>
    /// <param name="embeddings">Embeddings result to include in the bundle.</param>
    /// <param name="forecast">Forecast result to include in the bundle.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<SuiteMLArtifactBundle> GenerateSuiteArtifactsAsync(
        MLAnalyticsResult analytics,
        MLEmbeddingsResult embeddings,
        MLForecastResult forecast,
        CancellationToken cancellationToken = default
    )
    {
        var input = new
        {
            analytics,
            embeddings,
            forecast,
        };

        // Artifacts generation only uses Python (no ONNX model for this)
        if (IsPythonAvailable())
        {
            try
            {
                var result = await RunPythonScriptAsync<SuiteMLArtifactBundle>(
                    "ml_suite_artifacts.py",
                    input,
                    cancellationToken
                );

                if (result is not null)
                    return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Python artifact generation failed, using fallback.");
                return BuildFallbackArtifacts();
            }
        }

        return BuildFallbackArtifacts();
    }

    /// <summary>
    /// Serializes <paramref name="bundle"/> to a timestamped JSON file under
    /// <c>&lt;stateRootPath&gt;/ml-artifacts/</c> and returns the full file path.
    /// </summary>
    /// <param name="bundle">Artifact bundle to export.</param>
    /// <param name="stateRootPath">Root of the Foundry state directory (from <c>FOUNDRY_STATE_ROOT</c>).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Absolute path of the exported JSON file.</returns>
    public async Task<string> ExportArtifactsAsync(
        SuiteMLArtifactBundle bundle,
        string stateRootPath,
        CancellationToken cancellationToken = default
    )
    {
        var artifactsDirectory = Path.Combine(stateRootPath, "ml-artifacts");
        Directory.CreateDirectory(artifactsDirectory);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(artifactsDirectory, $"suite-artifacts-{timestamp}.json");

        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        return filePath;
    }

    /// <summary>
    /// Invalidates the result cache, forcing the next call to re-run inference.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedEmbeddings = null;
    }

    private bool IsPythonAvailable()
    {
        if (_pythonAvailable.HasValue)
            return _pythonAvailable.Value;

        lock (_pythonCheckLock)
        {
            if (_pythonAvailable.HasValue)
                return _pythonAvailable.Value;

            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                process.WaitForExit(5000);
                _pythonAvailable = process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Python not available on this system.");
                _pythonAvailable = false;
            }

            return _pythonAvailable.Value;
        }
    }

    private async Task<T?> RunPythonScriptAsync<T>(
        string scriptName,
        object input,
        CancellationToken cancellationToken
    )
    {
        var scriptPath = Path.Combine(_scriptsDirectory, scriptName);
        if (!File.Exists(scriptPath))
        {
            return default;
        }

        var inputJson = JsonSerializer.Serialize(input, _jsonOptions);
        var tempInputPath = Path.Combine(Path.GetTempPath(), $"foundry-ml-{Guid.NewGuid()}.json");

        try
        {
            await File.WriteAllTextAsync(tempInputPath, inputJson, cancellationToken);

            // Enforce script timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ScriptTimeout);

            var output = await _resiliencePipeline.ExecuteAsync(
                async ct => await _processRunner.RunAsync(
                    "python",
                    $"\"{scriptPath}\" --input \"{tempInputPath}\"",
                    _scriptsDirectory,
                    ct
                ),
                timeoutCts.Token
            );

            if (string.IsNullOrWhiteSpace(output))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(output, _jsonOptions);
        }
        finally
        {
            try
            {
                if (File.Exists(tempInputPath))
                {
                    File.Delete(tempInputPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Best effort cleanup of temp file failed.");
            }
        }
    }

    private void CacheEmbeddings(MLEmbeddingsResult result)
    {
        _cachedEmbeddings = result;
        _embeddingsCachedAt = DateTimeOffset.Now;
    }

    private static MLEmbeddingsResult BuildFallbackEmbeddings() =>
        new()
        {
            Ok = false,
            Engine = "fallback",
        };

    private static SuiteMLArtifactBundle BuildFallbackArtifacts() =>
        new()
        {
            Ok = false,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
}
