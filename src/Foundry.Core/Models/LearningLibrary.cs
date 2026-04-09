namespace Foundry.Models;

/// <summary>
/// Snapshot of the knowledge library scanned by <see cref="Foundry.Services.KnowledgeImportService"/>.
/// Aggregates all <see cref="LearningDocument"/> entries found across one or more source root directories.
/// </summary>
public sealed class LearningLibrary
{
    /// <summary>Absolute path to the primary knowledge library root directory.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>Whether the <see cref="RootPath"/> directory exists on disk.</summary>
    public bool Exists { get; init; }

    /// <summary>All source root directories that were scanned (primary + additional paths).</summary>
    public IReadOnlyList<string> SourceRoots { get; init; } = Array.Empty<string>();

    /// <summary>All documents discovered across all source roots.</summary>
    public IReadOnlyList<LearningDocument> Documents { get; init; } = Array.Empty<LearningDocument>();

    /// <summary>Top topic labels extracted from the library's documents, sorted by frequency.</summary>
    public IReadOnlyList<string> TopicHeadlines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Human-readable summary of the library state, suitable for display in the Discord bot or logs.
    /// </summary>
    public string Summary
    {
        get
        {
            if (!Exists)
            {
                return $"Knowledge folder not found at {RootPath}.";
            }

            if (Documents.Count == 0)
            {
                return $"Knowledge library is ready, but no supported study files were found in the scanned sources.";
            }

            return
                $"{Documents.Count} knowledge files loaded from {SourceRoots.Count} source{(SourceRoots.Count == 1 ? string.Empty : "s")}. Dominant topics: {string.Join(", ", TopicHeadlines.Take(5))}.";
        }
    }
}
