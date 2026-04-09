using System.Text.Json.Serialization;

namespace Foundry.Models;

/// <summary>
/// Represents a single knowledge document scanned from the learning library.
/// Documents are loaded by <see cref="Foundry.Services.KnowledgeImportService"/> and consumed
/// by the ML pipeline and knowledge search subsystems.
/// </summary>
public sealed class LearningDocument
{
    /// <summary>Absolute path to the root directory this document was scanned from.</summary>
    public string SourceRootPath { get; init; } = string.Empty;

    /// <summary>Short human-readable label for the source root (e.g. the folder name).</summary>
    public string SourceRootLabel { get; init; } = string.Empty;

    /// <summary>File name including extension (e.g. "networking-notes.md").</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Absolute path to the document on disk.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>Path relative to <see cref="SourceRootPath"/>, used as the stable document identifier.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Document kind (e.g. "markdown", "pdf", "text").</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>When the document file was last modified on disk.</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>Total character count of the extracted text.</summary>
    public int CharacterCount { get; init; }

    /// <summary>Topics detected in the document (e.g. from front-matter or LLM extraction).</summary>
    public IReadOnlyList<string> Topics { get; init; } = Array.Empty<string>();

    /// <summary>One-paragraph summary of the document's content.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Full plain-text content extracted from the document.</summary>
    public string ExtractedText { get; init; } = string.Empty;

    /// <summary>Structured tables extracted from the document (e.g. PDF tables via Docling).</summary>
    public IReadOnlyList<ExtractedTable> Tables { get; init; } = Array.Empty<ExtractedTable>();

    /// <summary>Figure descriptions extracted from the document (e.g. via Docling OCR/captions).</summary>
    public IReadOnlyList<ExtractedFigure> Figures { get; init; } = Array.Empty<ExtractedFigure>();

    /// <summary>
    /// Compact one-line representation for use in LLM prompts.
    /// Includes source label, relative path, kind, up to four topics, and the summary.
    /// </summary>
    public string PromptSummary =>
        $"[{SourceRootLabel}] {RelativePath} ({Kind}) | topics: {string.Join(", ", Topics.Take(4))} | {Summary}";

    /// <summary>
    /// Human-readable one-line display string for UI or logging.
    /// Includes source label, relative path, kind, character count, and up to four topics.
    /// </summary>
    public string DisplaySummary =>
        $"[{SourceRootLabel}] {RelativePath} | {Kind} | {CharacterCount} chars | {string.Join(", ", Topics.Take(4))}";
}

/// <summary>A table extracted from a document, represented as headers and rows.</summary>
public sealed class ExtractedTable
{
    [JsonPropertyName("headers")]
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();

    [JsonPropertyName("rows")]
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = Array.Empty<IReadOnlyList<object?>>();

    /// <summary>Renders the table as a Markdown table string.</summary>
    public string ToMarkdown()
    {
        if (Headers.Count == 0)
            return string.Empty;

        var markdownBuilder = new System.Text.StringBuilder();
        markdownBuilder.AppendLine("| " + string.Join(" | ", Headers) + " |");
        markdownBuilder.AppendLine("| " + string.Join(" | ", Headers.Select(_ => "---")) + " |");
        foreach (var row in Rows)
        {
            var cells = row.Select(c => c?.ToString() ?? "").ToList();
            // Pad or truncate to match header count
            while (cells.Count < Headers.Count) cells.Add("");
            markdownBuilder.AppendLine("| " + string.Join(" | ", cells.Take(Headers.Count)) + " |");
        }
        return markdownBuilder.ToString().TrimEnd();
    }
}

/// <summary>A figure/image extracted from a document with a description.</summary>
public sealed class ExtractedFigure
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
