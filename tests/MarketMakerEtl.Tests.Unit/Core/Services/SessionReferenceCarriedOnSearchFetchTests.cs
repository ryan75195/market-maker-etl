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
public class SessionReferenceCarriedOnSearchFetchTests
{
    private const string BlobUri =
        "http://127.0.0.1:10000/devstoreaccount1/html/job-1/page.html";

    private const string SearchUrl = "https://www.ebay.co.uk/sch/i.html";

    private const string ConfiguredSessionReference = "operator-session-token";

    [Test]
    public async Task Should_carry_the_configured_session_reference_on_the_new_job_request()
    {
        var handler = new CapturingNewJobHandler(BlobUri);
        var content = Substitute.For<IScrapeContentStore>();
        content.GetHtml(BlobUri, Arg.Any<CancellationToken>()).Returns("<html></html>");

        await CreateClient(handler, content, OptionsFor(ConfiguredSessionReference))
            .GetPageHtml(SearchUrl, CancellationToken.None);

        Assert.That(ReadSessionReference(handler.NewJobBody!), Is.EqualTo(ConfiguredSessionReference));
    }

    private static ScrapeClientOptions OptionsFor(string? sessionReference) => new(
        "http://scraper.test",
        "key",
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(1),
        sessionReference);

    private static HttpScrapeClient CreateClient(
        HttpMessageHandler handler,
        IScrapeContentStore content,
        ScrapeClientOptions options) =>
        new(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance);

    private static string? ReadSessionReference(string body)
    {
        using var document = JsonDocument.Parse(body);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("sessionReference", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind != JsonValueKind.Null)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private sealed class CapturingNewJobHandler : HttpMessageHandler
    {
        private readonly string _blobUri;
        private int _statusCalls;

        public CapturingNewJobHandler(string blobUri)
        {
            _blobUri = blobUri;
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

            if (path == "/api/GetStatus")
            {
                _statusCalls++;
                return Json(_statusCalls == 1
                    ? "{\"job\":{\"jobId\":\"job-1\",\"status\":\"processing\"}}"
                    : "{\"job\":{\"jobId\":\"job-1\",\"status\":\"success\"}}");
            }

            if (path == "/api/GetResults")
            {
                return Json($"[{{\"blobUri\":\"{_blobUri}\"}}]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
