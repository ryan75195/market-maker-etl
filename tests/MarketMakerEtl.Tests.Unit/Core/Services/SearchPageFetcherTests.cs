using MarketMakerEtl.Core.Interfaces;
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
    public async Task Should_report_a_failure_with_the_challenge_message_after_exhausting_attempts()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ChallengeHtml);

        var fetcher = new SearchPageFetcher(
            client, new MercariSearchParser(), maxAttempts: 3, baseDelaySeconds: 0, NullLogger.Instance);

        var outcome = await fetcher.Fetch("https://search", Band, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.Null);
            Assert.That(outcome.Failure!.Value.ErrorMessage, Is.EqualTo("Cloudflare challenge page"));
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
}
