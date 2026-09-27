using System.Net;
using System.Text;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetcherHealthClientTests
{
    private static FetcherOptions Options() => new("http://fetcher.test", TimeSpan.FromSeconds(30));

    [Test]
    public async Task Should_report_reachable_when_the_sidecar_returns_ok()
    {
        var handler = new StubHealthHandler(_ => Json("""{"status":"ok"}"""));
        var client = new FetcherHealthClient(new HttpClient(handler), Options());

        var reachable = await client.CheckSidecarReachable(CancellationToken.None);

        Assert.That(reachable, Is.True);
    }

    [Test]
    public async Task Should_report_unreachable_when_the_sidecar_returns_a_non_ok_status()
    {
        var handler = new StubHealthHandler(_ => Json("""{"status":"degraded"}"""));
        var client = new FetcherHealthClient(new HttpClient(handler), Options());

        var reachable = await client.CheckSidecarReachable(CancellationToken.None);

        Assert.That(reachable, Is.False);
    }

    [Test]
    public async Task Should_report_unreachable_when_the_sidecar_returns_an_error_status_code()
    {
        var handler = new StubHealthHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = new FetcherHealthClient(new HttpClient(handler), Options());

        var reachable = await client.CheckSidecarReachable(CancellationToken.None);

        Assert.That(reachable, Is.False);
    }

    [Test]
    public async Task Should_report_unreachable_when_the_sidecar_cannot_be_reached()
    {
        var handler = new StubHealthHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new FetcherHealthClient(new HttpClient(handler), Options());

        var reachable = await client.CheckSidecarReachable(CancellationToken.None);

        Assert.That(reachable, Is.False);
    }

    [Test]
    public async Task Should_report_unreachable_when_the_sidecar_returns_a_malformed_body()
    {
        var handler = new StubHealthHandler(_ => Json("not json"));
        var client = new FetcherHealthClient(new HttpClient(handler), Options());

        var reachable = await client.CheckSidecarReachable(CancellationToken.None);

        Assert.That(reachable, Is.False);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHealthHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHealthHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
