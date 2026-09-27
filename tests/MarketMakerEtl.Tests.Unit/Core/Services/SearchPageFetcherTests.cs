using System.Net;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SearchPageFetcherTests
{
    private const string ChallengeHtml = """
        <html>
          <head><title>Just a moment...</title></head>
          <body>
            <script src="/cdn-cgi/challenge-platform/h/g/orchestrate/jsch/v1"></script>
          </body>
        </html>
        """;

    private const string PayloadWithOneListing = """
        {"data":{"search":{"count":1,"itemsList":[{"id":"m1","name":"PS5","status":"on_sale","price":1000}]}}}
        """;

    private static readonly PriceBand Band = PriceBand.Unfiltered;

    [Test]
    public async Task Should_retry_past_challenge_pages_and_succeed_once_a_real_payload_arrives()
    {
        var callCount = 0;
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return Task.FromResult(callCount <= 2 ? ChallengeHtml : PayloadWithOneListing);
        });

        var fetcher = new SearchPageFetcher(
            client, new MercariSearchParser(), maxAttempts: 5, baseDelaySeconds: 0, NullLogger.Instance);

        var outcome = await fetcher.Fetch("https://search", Band, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Failure, Is.Null);
            Assert.That(outcome.Result!.Listings, Has.Count.EqualTo(1));
            Assert.That(callCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Should_report_a_failure_with_the_unrecognised_payload_message_after_exhausting_attempts()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ChallengeHtml);

        var fetcher = new SearchPageFetcher(
            client, new MercariSearchParser(), maxAttempts: 3, baseDelaySeconds: 0, NullLogger.Instance);

        var outcome = await fetcher.Fetch("https://search", Band, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.Null);
            Assert.That(outcome.Failure!.Value.ErrorMessage, Is.EqualTo("Unrecognised search payload"));
        });
        await client.Received(3).GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_propagate_cancellation_instead_of_returning_a_failure()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new OperationCanceledException());

        var fetcher = new SearchPageFetcher(
            client, new MercariSearchParser(), maxAttempts: 3, baseDelaySeconds: 0, NullLogger.Instance);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fetcher.Fetch("https://search", Band, CancellationToken.None));
    }

    [Test]
    public async Task Should_return_a_failed_outcome_after_exhausting_attempts_when_every_fetch_times_out()
    {
        var handler = new NeverTerminatingScrapeHandler();
        var options = new FetcherOptions("http://fetcher.test", TimeSpan.FromMilliseconds(20));
        var client = new FetcherScrapeClient(new HttpClient(handler), options, Substitute.For<IFetchOutcomeMonitor>());

        var fetcher = new SearchPageFetcher(
            client, new MercariSearchParser(), maxAttempts: 2, baseDelaySeconds: 0, NullLogger.Instance);

        var outcome = await fetcher.Fetch("https://search", Band, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.Null);
            Assert.That(outcome.Failure, Is.Not.Null);
            Assert.That(outcome.Failure!.Value.ErrorMessage, Does.Contain("timed out"));
        });
    }

    private sealed class NeverTerminatingScrapeHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
