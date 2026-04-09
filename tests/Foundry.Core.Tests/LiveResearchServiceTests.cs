using System.Net;
using Foundry.Services;
using Polly;
using Xunit;

namespace Foundry.Core.Tests;

/// <summary>
/// Unit tests for LiveResearchService covering input guards, graceful HTTP failure,
/// HTML extraction, and resilience pipeline retry behavior.
/// </summary>
public sealed class LiveResearchServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// HttpMessageHandler stub that returns a fixed response and records how many times it was called.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        public int CallCount { get; private set; }

        public StubHandler(string content = "", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content),
            });
        }
    }

    /// <summary>
    /// HttpMessageHandler stub that throws <see cref="HttpRequestException"/> on every call
    /// and counts how many times it was invoked (for verifying retry behavior).
    /// </summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("Simulated network failure");
        }
    }

    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { Timeout = TimeSpan.FromSeconds(5) };

    // -------------------------------------------------------------------------
    // SearchAsync — input guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_NullOrWhitespaceQuery_ReturnsEmpty()
    {
        var svc = new LiveResearchService();

        Assert.Empty(await svc.SearchAsync(null!));
        Assert.Empty(await svc.SearchAsync(""));
        Assert.Empty(await svc.SearchAsync("   "));
    }

    [Fact]
    public async Task SearchAsync_HttpFails_ReturnsEmpty()
    {
        var handler = new FailingHandler();
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var result = await svc.SearchAsync("test query");

        Assert.Empty(result);
        Assert.True(handler.CallCount >= 1);
    }

    [Fact]
    public async Task SearchAsync_ParsesDuckDuckGoHtml_ReturnsResults()
    {
        const string html = """
            <html><body>
              <div class="result__body">
                <a class="result__a" href="https://example.com/page">Example Page</a>
                <div class="result__snippet">A short description of the page.</div>
              </div>
              <div class="result__body">
                <a class="result__a" href="https://other.com/">Other Site</a>
                <div class="result__snippet">Another snippet here.</div>
              </div>
            </body></html>
            """;

        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var results = await svc.SearchAsync("anything");

        Assert.Equal(2, results.Count);
        Assert.Equal("https://example.com/page", results[0].Url);
        Assert.Equal("Example Page", results[0].Title);
        Assert.Contains("short description", results[0].Preview);
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        var items = string.Concat(Enumerable.Range(1, 10).Select(i => $"""
            <div class="result__body">
              <a class="result__a" href="https://site{i}.com/">Site {i}</a>
              <div class="result__snippet">Snippet {i}.</div>
            </div>
            """));
        var html = $"<html><body>{items}</body></html>";

        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var results = await svc.SearchAsync("query", maxResults: 3);

        Assert.Equal(3, results.Count);
    }

    // -------------------------------------------------------------------------
    // ExtractPreviewAsync — input guards and HTML extraction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtractPreviewAsync_EmptyUrl_ReturnsEmpty()
    {
        var svc = new LiveResearchService();

        Assert.Equal(string.Empty, await svc.ExtractPreviewAsync(null!));
        Assert.Equal(string.Empty, await svc.ExtractPreviewAsync(""));
        Assert.Equal(string.Empty, await svc.ExtractPreviewAsync("  "));
    }

    [Fact]
    public async Task ExtractPreviewAsync_HttpFails_ReturnsEmpty()
    {
        var handler = new FailingHandler();
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var result = await svc.ExtractPreviewAsync("https://unreachable.invalid/");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ExtractPreviewAsync_ValidHtml_ExtractsBodyText()
    {
        const string html = "<html><head><title>Test</title></head><body><p>Hello world</p></body></html>";
        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var preview = await svc.ExtractPreviewAsync("https://example.com/");

        Assert.Contains("Hello world", preview);
    }

    [Fact]
    public async Task ExtractPreviewAsync_ScriptAndStyleRemoved_NotInPreview()
    {
        const string html = """
            <html><body>
              <script>alert('xss')</script>
              <style>.cls { color: red; }</style>
              <p>Visible content</p>
            </body></html>
            """;
        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var preview = await svc.ExtractPreviewAsync("https://example.com/");

        Assert.DoesNotContain("alert", preview);
        Assert.DoesNotContain("color: red", preview);
        Assert.Contains("Visible content", preview);
    }

    [Fact]
    public async Task ExtractPreviewAsync_LongPage_TruncatesToMaxPreviewLength()
    {
        var longText = new string('a', 2000);
        var html = $"<html><body><p>{longText}</p></body></html>";
        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var preview = await svc.ExtractPreviewAsync("https://example.com/");

        Assert.True(preview.Length <= 900);
    }

    // -------------------------------------------------------------------------
    // EnrichSourcesAsync — input guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnrichSourcesAsync_NullOrEmpty_ReturnsEmpty()
    {
        var svc = new LiveResearchService();

        Assert.Empty(await svc.EnrichSourcesAsync(null!));
        Assert.Empty(await svc.EnrichSourcesAsync([]));
    }

    [Fact]
    public async Task EnrichSourcesAsync_AllUrlsFail_ReturnsEmpty()
    {
        var handler = new FailingHandler();
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var result = await svc.EnrichSourcesAsync(
            ["https://fail1.invalid/", "https://fail2.invalid/"]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EnrichSourcesAsync_SuccessfulFetch_ReturnsResults()
    {
        const string html = "<html><head><title>My Page</title></head><body><p>Page body text.</p></body></html>";
        var handler = new StubHandler(html);
        var svc = new LiveResearchService(httpClient: MakeClient(handler));

        var results = await svc.EnrichSourcesAsync(["https://example.com/"]);

        Assert.Single(results);
        Assert.Equal("https://example.com/", results[0].Url);
        Assert.Equal("My Page", results[0].Title);
        Assert.Contains("Page body text", results[0].Preview);
    }

    // -------------------------------------------------------------------------
    // Retry behavior — pipeline is invoked on failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithRetryPipeline_RetriesOnTransientFailure()
    {
        var handler = new FailingHandler();
        // 2 retries → 3 total attempts
        var pipeline = FoundryResiliencePipelines.BuildWebResearchPipeline();
        var svc = new LiveResearchService(
            httpClient: MakeClient(handler),
            resiliencePipeline: pipeline);

        var result = await svc.SearchAsync("retry test");

        Assert.Empty(result); // ultimately fails but retried
        // 1 initial + 2 retries = 3 attempts
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ExtractPreviewAsync_WithRetryPipeline_RetriesOnTransientFailure()
    {
        var handler = new FailingHandler();
        var pipeline = FoundryResiliencePipelines.BuildWebResearchPipeline();
        var svc = new LiveResearchService(
            httpClient: MakeClient(handler),
            resiliencePipeline: pipeline);

        var result = await svc.ExtractPreviewAsync("https://example.com/");

        Assert.Equal(string.Empty, result);
        Assert.Equal(3, handler.CallCount);
    }
}
