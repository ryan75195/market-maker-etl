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
public class FetchRequestWithoutSessionReferenceTests
{
    private const string BlobUri =
        "http://127.0.0.1:10000/devstoreaccount1/html/job-1/page.html";

    private const string SearchUrl = "https://www.ebay.co.uk/sch/i.html";

    [Test]
    public async Task Should_omit_the_session_reference_when_none_is_configured()
    {
        var handler = new CapturingNewJobHandler(BlobUri);
        var content = Substitute.For<IScrapeContentStore>();
        content.GetHtml(BlobUri, Arg.Any<CancellationToken>()).Returns("<html></html>");

        await CreateClient(handler, content, OptionsWithoutSessionReference())
            .GetPageHtml(SearchUrl, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(HasSessionReference(handler.NewJobBody!), Is.False);
            Assert.That(handler.NewJobBody, Does.Contain(SearchUrl));
        });
    }

    private static ScrapeClientOptions OptionsWithoutSessionReference() => new(
        "http://scraper.test",
        "key",
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(1));

    private static HttpScrapeClient CreateClient(
        HttpMessageHandler handler,
        IScrapeContentStore content,
        ScrapeClientOptions options) =>
        new(new HttpClient(handler), options, content, NullLogger<HttpScrapeClient>.Instance);

    private static bool HasSessionReference(string body)
    {
        using var document = JsonDocument.Parse(body);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("sessionReference", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
