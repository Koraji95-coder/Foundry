namespace Foundry.Services;

/// <summary>
/// Abstraction over an LLM backend (e.g. Ollama).
/// Provides text generation, JSON generation, embedding, and health-check operations.
/// Implement this interface to add a new model provider backend.
/// </summary>
public interface IModelProvider
{
    /// <summary>Unique, machine-readable identifier for this provider (e.g. "ollama").</summary>
    string ProviderId { get; }

    /// <summary>Human-readable label for this provider (e.g. "Ollama (local)").</summary>
    string ProviderLabel { get; }

    /// <summary>
    /// Returns the names of all models currently installed in this provider's backend.
    /// Returns an empty list if the backend is unreachable.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<string>> GetInstalledModelsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Generates a plain-text response from the model using a system and user prompt.
    /// </summary>
    /// <param name="model">Model name to use for generation (e.g. "qwen3:8b").</param>
    /// <param name="systemPrompt">System-role message that sets the model's behavior.</param>
    /// <param name="userPrompt">User-role message containing the request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The trimmed text response from the model.</returns>
    Task<string> GenerateAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Generates a structured JSON response from the model and deserializes it to <typeparamref name="T"/>.
    /// Returns null if the model returns empty output or the JSON cannot be deserialized.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize the JSON response into.</typeparam>
    /// <param name="model">Model name to use for generation.</param>
    /// <param name="systemPrompt">System-role message that sets the model's behavior.</param>
    /// <param name="userPrompt">User-role message containing the request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<T?> GenerateJsonAsync<T>(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks whether the model provider backend is reachable.
    /// Returns true if the provider responds to a lightweight ping.
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for the given text using the specified model.
    /// Returns null if the provider does not support embeddings or is unavailable.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="model">
    /// Optional embedding model override. Falls back to the provider's default embedding model when null.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<float[]?> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<float[]?>(null);
    }
}
