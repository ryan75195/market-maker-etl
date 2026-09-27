using System.Net;
using System.Text;
using System.Text.Json;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetcherScrapeClientTests
{
    private const string ItemUrl = "https://www.mercari.com/us/item/m1/";

    private static FetcherOptions Options(int timeoutSeconds = 30) =>
        new("http://fetcher.test", TimeSpan.FromSeconds(timeoutSeconds));

    private static FetcherScrapeClient CreateClient(
        HttpMessageHandler handler, IFetchOutcomeMonitor outcomeMonitor, int timeoutSeconds = 30) =>
        new(new HttpClient(handler), Options(timeoutSeconds), outcomeMonitor);

    [Test]
    public async Task Should_post_the_requested_url_and_return_the_raw_body_on_success()
    {
        var handler = new StubFetcherHandler(_ => Json("""{"kind":"item","body":"{\"data\":{}}"}"""));
        var outcomeMonitor = Substitute.For<IFetchOutcomeMonitor>();
        var client = CreateClient(handler, outcomeMonitor);

        var body = await client.GetPageHtml(ItemUrl, CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(body, Is.EqualTo("{\"data\":{}}"));
            Assert.That(document.RootElement.GetProperty("url").GetString(), Is.EqualTo(ItemUrl));
        });
        outcomeMonitor.Received(1).Record(FetchOutcomeKind.Success);
    }

    [Test]
    public void Should_throw_listing_not_found_for_a_404_response()
    {
        var handler = new StubFetcherHandler(_ => Json("""{"error":"not_found"}""", HttpStatusCode.NotFound));
        var outcomeMonitor = Substitute.For<IFetchOutcomeMonitor>();
        var client = CreateClient(handler, outcomeMonitor);

        Assert.ThrowsAsync<ListingNotFoundException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
        outcomeMonitor.Received(1).Record(FetchOutcomeKind.NotFound);
    }

    [Test]
    public void Should_throw_fetch_infrastructure_unavailable_for_a_proxy_unavailable_response()
    {
        var handler = new StubFetcherHandler(_ => Json("""{"error":"proxy_unavailable"}""", HttpStatusCode.BadGateway));
        var outcomeMonitor = Substitute.For<IFetchOutcomeMonitor>();
        var client = CreateClient(handler, outcomeMonitor);

        Assert.ThrowsAsync<FetchInfrastructureUnavailableException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
        outcomeMonitor.Received(1).Record(FetchOutcomeKind.Infrastructure);
    }

    [Test]
    public void Should_throw_fetch_infrastructure_unavailable_for_an_upstream_blocked_response()
    {
        var handler = new StubFetcherHandler(_ => Json("""{"error":"upstream_blocked"}""", HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>());

        Assert.ThrowsAsync<FetchInfrastructureUnavailableException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_throw_fetch_failed_for_an_upstream_error_response()
    {
        var handler = new StubFetcherHandler(
            _ => Json("""{"error":"upstream_error","detail":"boom"}""", HttpStatusCode.BadGateway));
        var outcomeMonitor = Substitute.For<IFetchOutcomeMonitor>();
        var client = CreateClient(handler, outcomeMonitor);

        var exception = Assert.ThrowsAsync<FetchFailedException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("boom"));
        outcomeMonitor.Received(1).Record(FetchOutcomeKind.Other);
    }

    [Test]
    public void Should_throw_fetch_failed_for_an_unsupported_url_response()
    {
        var handler = new StubFetcherHandler(_ => Json("""{"error":"unsupported_url"}""", HttpStatusCode.BadRequest));
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>());

        Assert.ThrowsAsync<FetchFailedException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_throw_fetch_failed_for_an_unexpected_status_code()
    {
        var handler = new StubFetcherHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>());

        Assert.ThrowsAsync<FetchFailedException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_throw_fetch_failed_for_a_malformed_success_body()
    {
        var handler = new StubFetcherHandler(_ => Json("not json"));
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>());

        Assert.ThrowsAsync<FetchFailedException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_throw_fetch_infrastructure_unavailable_when_the_sidecar_cannot_be_reached()
    {
        var handler = new StubFetcherHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>());

        Assert.ThrowsAsync<FetchInfrastructureUnavailableException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_throw_fetch_infrastructure_unavailable_rather_than_operation_cancelled_on_timeout()
    {
        var handler = new StubFetcherHandler(neverResponds: true);
        var client = CreateClient(handler, Substitute.For<IFetchOutcomeMonitor>(), timeoutSeconds: 1);

        Assert.ThrowsAsync<FetchInfrastructureUnavailableException>(async () =>
            await client.GetPageHtml(ItemUrl, CancellationToken.None));
    }

    [Test]
    public void Should_propagate_caller_cancellation()
    {
        var handler = new StubFetcherHandler(neverResponds: true);
        var outcomeMonitor = Substitute.For<IFetchOutcomeMonitor>();
        var client = CreateClient(handler, outcomeMonitor, timeoutSeconds: 30);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await client.GetPageHtml(ItemUrl, cts.Token));
        outcomeMonitor.DidNotReceiveWithAnyArgs().Record(default);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubFetcherHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _respond;
        private readonly bool _neverResponds;

        public StubFetcherHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public StubFetcherHandler(bool neverResponds)
        {
            _neverResponds = neverResponds;
        }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_neverResponds)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return _respond!(request);
        }
    }
}
