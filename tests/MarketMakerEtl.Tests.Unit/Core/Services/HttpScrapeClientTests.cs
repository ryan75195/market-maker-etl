using System.Net;
using System.Text;
using System.Text.Json;
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
    public void Should_surface_the_upstream_failure_reason_when_a_job_fails()
    {
        var handler = new StubScrapeHandler(BlobUri, failJob: true, failureReason: "Blocked: Captcha (21KB)");
        var content = Substitute.For<IScrapeContentStore>();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateClient(handler, content).GetPageHtml("https://www.mercari.com/search/?keyword=test", CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("Blocked: Captcha (21KB)"));
    }

    [Test]
    public void Should_throw_when_no_content_is_stored()
    {
        var handler = new StubScrapeHandler(blobUri: null);
        var content = Substitute.For<IScrapeContentStore>();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateClient(handler, content).GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));
    }

    [Test]
    public void Should_throw_a_timeout_exception_naming_the_url_when_the_job_never_reaches_a_terminal_state()
    {
        var handler = new StubScrapeHandler(BlobUri, neverTerminates: true);
        var content = Substitute.For<IScrapeContentStore>();
        var options = new ScrapeClientOptions(
            "http://scraper.test", "key", TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5));
        var client = new HttpScrapeClient(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance);

        var exception = Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("https://www.ebay.co.uk/sch/i.html"));
    }

    [Test]
    public void Should_propagate_operation_cancelled_when_the_callers_token_is_cancelled()
    {
        var handler = new StubScrapeHandler(BlobUri, neverTerminates: true);
        var content = Substitute.For<IScrapeContentStore>();
        var options = new ScrapeClientOptions(
            "http://scraper.test", "key", TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(5));
        var client = new HttpScrapeClient(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await client.GetPageHtml("https://www.ebay.co.uk/sch/i.html", cts.Token));
    }

    [Test]
    public async Task Should_pin_the_new_job_wire_field_names()
    {
        var handler = new StubScrapeHandler(BlobUri);
        var content = Substitute.For<IScrapeContentStore>();
        content.GetHtml(BlobUri, Arg.Any<CancellationToken>()).Returns("<html></html>");
        var options = new ScrapeClientOptions(
            "http://scraper.test",
            "key",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(1),
            "operator-session-token");

        await new HttpScrapeClient(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance)
            .GetPageHtml("https://www.ebay.co.uk/sch/i.html", CancellationToken.None);

        using var document = JsonDocument.Parse(handler.NewJobBody!);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.That(names, Is.EqualTo(new[] { "Urls", "SessionReference" }));
    }

    [Test]
    public async Task Should_not_send_session_reference_for_mercari_fetches()
    {
        var handler = new StubScrapeHandler(BlobUri);
        var content = Substitute.For<IScrapeContentStore>();
        content.GetHtml(BlobUri, Arg.Any<CancellationToken>()).Returns("<html></html>");
        var options = new ScrapeClientOptions(
            "http://scraper.test",
            "key",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(1),
            "operator-session-token");

        await new HttpScrapeClient(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance)
            .GetPageHtml("https://www.mercari.com/us/item/m12345/", CancellationToken.None);

        using var document = JsonDocument.Parse(handler.NewJobBody!);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.That(names, Is.EqualTo(new[] { "Urls" }));
    }

    private static HttpScrapeClient CreateClient(HttpMessageHandler handler, IScrapeContentStore content) =>
        new(new HttpClient(handler), Options, content, NullLogger<HttpScrapeClient>.Instance);

    private sealed class StubScrapeHandler : HttpMessageHandler
    {
        private readonly string? _blobUri;
        private readonly bool _failJob;
        private readonly string? _failureReason;
        private readonly bool _neverTerminates;
        private int _statusCalls;

        public StubScrapeHandler(
            string? blobUri, bool failJob = false, string? failureReason = null, bool neverTerminates = false)
        {
            _blobUri = blobUri;
            _failJob = failJob;
            _failureReason = failureReason;
            _neverTerminates = neverTerminates;
        }

        public string? NewJobBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/NewJob")
            {
                NewJobBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("{\"jobId\":\"job-1\"}");
            }

            var response = path switch
            {
                "/api/GetStatus" => Json(StatusBody()),
                "/api/GetResults" => Json(ResultsBody()),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return response;
        }

        private string ResultsBody()
        {
            if (_failJob && _failureReason is not null)
            {
                return $"[{{\"error\":\"{_failureReason}\"}}]";
            }

            return _blobUri is null
                ? "[{\"blobUri\":null}]"
                : $"[{{\"blobUri\":\"{_blobUri}\"}}]";
        }

        private string StatusBody()
        {
            _statusCalls++;
            if (_failJob)
            {
                return "{\"job\":{\"jobId\":\"job-1\",\"status\":\"failure\"}}";
            }

            if (_neverTerminates)
            {
                return "{\"job\":{\"jobId\":\"job-1\",\"status\":\"processing\"}}";
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
