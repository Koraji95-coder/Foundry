using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Foundry.Services;

/// <summary>
/// Result returned by web research operations.
/// </summary>
public sealed record ResearchResult(string Url, string Title, string Preview);

/// <summary>
/// Performs live web research using DuckDuckGo and page content extraction.
/// All external HTTP calls are wrapped in a Polly resilience pipeline (exponential backoff retry + timeout).
/// </summary>
public sealed class LiveResearchService
{
    internal const int MaxPreviewLength = 900;

    private const string ExcludedElementsSelector = "script, style, nav, footer, header";

    private static readonly HtmlParser _htmlParser = new();

    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<LiveResearchService> _logger;

    public LiveResearchService(
        HttpClient? httpClient = null,
        ResiliencePipeline? resiliencePipeline = null,
        ILogger<LiveResearchService>? logger = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _resiliencePipeline = resiliencePipeline ?? ResiliencePipeline.Empty;
        _logger = logger ?? NullLogger<LiveResearchService>.Instance;
    }

    /// <summary>
    /// Searches DuckDuckGo for the given query and returns a list of result URLs, titles, and snippets.
    /// The HTTP call is wrapped in the resilience pipeline for retry on transient failures.
    /// </summary>
    public async Task<IReadOnlyList<ResearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";

        try
        {
            var html = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                _logger.LogDebug("Fetching DuckDuckGo results for query: {Query}", query);
                return await _httpClient.GetStringAsync(url, ct);
            }, cancellationToken);

            return ParseDuckDuckGoResults(html, maxResults);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DuckDuckGo search failed for query: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// Enriches a list of source URLs by fetching each page in parallel and extracting a preview.
    /// Each fetch is independently wrapped in the resilience pipeline.
    /// </summary>
    public async Task<IReadOnlyList<ResearchResult>> EnrichSourcesAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        if (urls is null || urls.Count == 0)
        {
            return [];
        }

        var tasks = urls.Select(url => FetchPreviewAsync(url, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.Where(r => r is not null).Cast<ResearchResult>().ToList();
    }

    /// <summary>
    /// Fetches a single URL and returns a plain-text preview of the page body.
    /// The HTTP call is wrapped in the resilience pipeline.
    /// </summary>
    public async Task<string> ExtractPreviewAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        try
        {
            var html = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                _logger.LogDebug("Fetching page for preview: {Url}", url);
                return await _httpClient.GetStringAsync(url, ct);
            }, cancellationToken);

            return ExtractTextFromHtml(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch or parse page: {Url}", url);
            return string.Empty;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<ResearchResult?> FetchPreviewAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                _logger.LogDebug("Enriching source: {Url}", url);
                return await _httpClient.GetStringAsync(url, ct);
            }, cancellationToken);

            var title = ExtractTitle(html);
            var preview = ExtractTextFromHtml(html);
            return new ResearchResult(url, title, preview);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich source: {Url}", url);
            return null;
        }
    }

    private static IReadOnlyList<ResearchResult> ParseDuckDuckGoResults(string html, int maxResults)
    {
        using var document = _htmlParser.ParseDocument(html);
        var results = new List<ResearchResult>();

        foreach (var result in document.QuerySelectorAll(".result__body").Take(maxResults))
        {
            var anchor = result.QuerySelector(".result__a");
            var snippet = result.QuerySelector(".result__snippet");

            var href = anchor?.GetAttribute("href") ?? string.Empty;
            var title = anchor?.TextContent?.Trim() ?? string.Empty;
            var preview = snippet?.TextContent?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(href) && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ResearchResult(href, title, preview));
            }
        }

        return results;
    }

    private static string ExtractTitle(string html)
    {
        using var document = _htmlParser.ParseDocument(html);
        return document.QuerySelector("title")?.TextContent?.Trim() ?? string.Empty;
    }

    private static string ExtractTextFromHtml(string html)
    {
        using var document = _htmlParser.ParseDocument(html);

        foreach (var element in document.QuerySelectorAll(ExcludedElementsSelector))
        {
            element.Remove();
        }

        var text = document.Body?.TextContent ?? string.Empty;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length > MaxPreviewLength ? text[..MaxPreviewLength] : text;
    }
}
