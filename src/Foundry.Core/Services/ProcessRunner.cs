using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Services;

/// <summary>
/// Runs external processes (Python scripts, CLI tools) and returns their standard output.
/// Used by <see cref="MLAnalyticsService"/> and <see cref="OllamaService"/> to invoke Python
/// ML scripts and the Ollama CLI respectively.
/// Throws <see cref="InvalidOperationException"/> when a process exits with a non-zero code.
/// </summary>
public sealed class ProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    /// <param name="logger">Optional logger. Defaults to a no-op logger when null.</param>
    public ProcessRunner(ILogger<ProcessRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<ProcessRunner>.Instance;
    }

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and returns stdout.
    /// </summary>
    /// <param name="fileName">Executable name or full path (e.g. "python", "ollama").</param>
    /// <param name="arguments">Command-line arguments passed verbatim to the process.</param>
    /// <param name="workingDirectory">
    /// Working directory for the process. Defaults to <see cref="Environment.CurrentDirectory"/> when null.
    /// </param>
    /// <param name="cancellationToken">Token to cancel waiting for the process to exit.</param>
    /// <returns>The full stdout output of the process.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process exits with a non-zero exit code; the message includes up to 500 characters of stderr.
    /// </exception>
    public async Task<string> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var stderrSnippet = string.IsNullOrWhiteSpace(error)
                ? "(no stderr)"
                : error.Trim().Length > 500
                    ? error.Trim()[..500] + "..."
                    : error.Trim();

            var message = $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}. stderr: {stderrSnippet}";
            _logger.LogWarning("Process failed: {Command} exit code {ExitCode}. stderr: {Stderr}",
                $"{fileName} {arguments}", process.ExitCode, stderrSnippet);
            throw new InvalidOperationException(message);
        }

        return output;
    }

    /// <summary>
    /// Checks whether Python 3 is available on this system.
    /// Returns the version string (e.g. "Python 3.12.0") or null if unavailable.
    /// </summary>
    public async Task<string?> CheckPythonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await RunAsync("python3", "--version", null, cancellationToken);
            var version = output.Trim();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch
        {
            // python3 not found, try python
        }

        try
        {
            var output = await RunAsync("python", "--version", null, cancellationToken);
            var version = output.Trim();
            if (!string.IsNullOrWhiteSpace(version) && version.StartsWith("Python 3", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }
        catch
        {
            // python not found either
        }

        return null;
    }
}
