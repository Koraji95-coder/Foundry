namespace Foundry.Models;

/// <summary>
/// Result produced by the PyTorch embeddings engine (or its TF-IDF fallback).
/// Contains document embedding vectors, pairwise similarity scores, and optional
/// query-relevance results. Similarity scores flow into the TensorFlow forecasting
/// engine as part of the EngineHandoff contract.
/// </summary>
public sealed class MLEmbeddingsResult
{
    /// <summary>Whether the embeddings engine ran successfully (false when falling back to TF-IDF).</summary>
    public bool Ok { get; init; }

    /// <summary>Which engine produced this result: "pytorch", "onnx", "tfidf", or "fallback".</summary>
    public string Engine { get; init; } = "tfidf";

    /// <summary>Embedding vector for each input document.</summary>
    public IReadOnlyList<MLDocumentEmbedding> Embeddings { get; init; } = Array.Empty<MLDocumentEmbedding>();

    /// <summary>Pairwise cosine similarity scores between documents.</summary>
    public IReadOnlyList<MLDocumentSimilarity> Similarities { get; init; } = Array.Empty<MLDocumentSimilarity>();

    /// <summary>Documents ranked by relevance to the optional query string, when provided.</summary>
    public IReadOnlyList<MLQueryResult> QueryResults { get; init; } = Array.Empty<MLQueryResult>();

    /// <summary>Error message from the PyTorch engine, if any.</summary>
    public string? PytorchError { get; init; }
}

/// <summary>Embedding vector produced for a single document.</summary>
public sealed class MLDocumentEmbedding
{
    /// <summary>Unique document identifier (typically the document's full file path).</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>Human-readable document title or file name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Dimensionality of the embedding vector.</summary>
    public int Dimensions { get; init; }

    /// <summary>The embedding vector values.</summary>
    public IReadOnlyList<double> Embedding { get; init; } = Array.Empty<double>();
}

/// <summary>Cosine similarity score between two documents.</summary>
public sealed class MLDocumentSimilarity
{
    /// <summary>Identifier of the first document in the pair.</summary>
    public string DocumentA { get; init; } = string.Empty;

    /// <summary>Identifier of the second document in the pair.</summary>
    public string DocumentB { get; init; } = string.Empty;

    /// <summary>Cosine similarity score in the range [−1, 1] (typically [0, 1] for text).</summary>
    public double Similarity { get; init; }
}

/// <summary>A single document ranked by relevance to a query.</summary>
public sealed class MLQueryResult
{
    /// <summary>Unique document identifier.</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>Human-readable document title or file name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Relevance score relative to the query, in the range [0, 1].</summary>
    public double Relevance { get; init; }
}
