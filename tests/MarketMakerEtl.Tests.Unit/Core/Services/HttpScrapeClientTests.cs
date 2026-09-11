using System.Net;
using System.Text;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class HttpScrapeClientTests
{
    private static ScrapeClientOptions Options => new(
        "http://scraper.test",
        "key",
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(1));

    [Test]
    public async Task Should_return_page_html_from_the_first_result_blob()
    {
        var handler = new StubScrapeHandler("<html><body>listing</body></html>");
        var client = new HttpScrapeClient(
            new HttpClient(handler),
            Options,
            NullLogger<HttpScrapeClient>.Instance);

        var html = await client.GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None);

        Assert.That(html, Is.EqualTo("<html><body>listing</body></html>"));
    }

    [Test]
    public void Should_throw_when_the_job_ends_in_failure()
    {
        var handler = new StubScrapeHandler("<html></html>", failJob: true);
        var client = new HttpScrapeClient(
            new HttpClient(handler),
            Options,
            NullLogger<HttpScrapeClient>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));
    }

    [Test]
    public void Should_throw_when_no_content_is_stored()
    {
        var handler = new StubScrapeHandler(null);
        var client = new HttpScrapeClient(
            new HttpClient(handler),
            Options,
            NullLogger<HttpScrapeClient>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));
    }

    private sealed class StubScrapeHandler : HttpMessageHandler
    {
        private const string BlobUri = "http://blob.test/page.html";
        private readonly string? _html;
        private readonly bool _failJob;

        public StubScrapeHandler(string? html, bool failJob = false)
        {
            _html = html;
            _failJob = failJob;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = path switch
            {
                "/api/NewJob" => Json("{\"jobId\":\"job-1\"}"),
                "/api/GetStatus" => Json(_failJob
                    ? "{\"job\":{\"jobId\":\"job-1\",\"status\":\"failure\"}}"
                    : "{\"job\":{\"jobId\":\"job-1\",\"status\":\"success\"}}"),
                "/api/GetResults" => Json(_html is null
                    ? "[{\"blobUri\":null}]"
                    : $"[{{\"blobUri\":\"{BlobUri}\"}}]"),
                "/page.html" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_html!, Encoding.UTF8, "text/html")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
