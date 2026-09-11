using System.Net;
using System.Text;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class HttpScrapeClientTests
{
    private const string BlobUri =
        "http://127.0.0.1:10000/devstoreaccount1/html/job-1/page.html";

    private static ScrapeClientOptions Options => new(
        "http://scraper.test",
        "key",
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(1));

    [Test]
    public async Task Should_return_page_html_from_the_first_result_blob()
    {
        var handler = new StubScrapeHandler(BlobUri);
        var content = Substitute.For<IScrapeContentStore>();
        content.GetHtml(BlobUri, Arg.Any<CancellationToken>()).Returns("<html><body>listing</body></html>");

        var html = await CreateClient(handler, content)
            .GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None);

        Assert.That(html, Is.EqualTo("<html><body>listing</body></html>"));
    }

    [Test]
    public void Should_throw_when_the_job_ends_in_failure()
    {
        var handler = new StubScrapeHandler(BlobUri, failJob: true);
        var content = Substitute.For<IScrapeContentStore>();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateClient(handler, content).GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));
    }

    [Test]
    public void Should_throw_when_no_content_is_stored()
    {
        var handler = new StubScrapeHandler(blobUri: null);
        var content = Substitute.For<IScrapeContentStore>();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateClient(handler, content).GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));
    }

    private static HttpScrapeClient CreateClient(HttpMessageHandler handler, IScrapeContentStore content) =>
        new(new HttpClient(handler), Options, content, NullLogger<HttpScrapeClient>.Instance);

    private sealed class StubScrapeHandler : HttpMessageHandler
    {
        private readonly string? _blobUri;
        private readonly bool _failJob;
        private int _statusCalls;

        public StubScrapeHandler(string? blobUri, bool failJob = false)
        {
            _blobUri = blobUri;
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
                "/api/GetStatus" => Json(StatusBody()),
                "/api/GetResults" => Json(_blobUri is null
                    ? "[{\"blobUri\":null}]"
                    : $"[{{\"blobUri\":\"{_blobUri}\"}}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        }

        private string StatusBody()
        {
            _statusCalls++;
            if (_failJob)
            {
                return "{\"job\":{\"jobId\":\"job-1\",\"status\":\"failure\"}}";
            }

            return _statusCalls == 1
                ? "{\"job\":{\"jobId\":\"job-1\",\"status\":\"processing\"}}"
                : "{\"job\":{\"jobId\":\"job-1\",\"status\":\"success\"}}";
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
