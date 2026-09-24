using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariPriceBandCollectorTests
{
    private const string SearchTerm = "ps5";

    [Test]
    public async Task Should_collect_every_listing_across_leaves_exactly_once_when_splitting_price_bands()
    {
        var listingA = new ListingSummary("m1", "A", 1m, "USD", "https://x/m1", false, null, null, null);
        var listingB = new ListingSummary("m2", "B", 1m, "USD", "https://x/m2", false, null, null, null);
        var collector = BuildCollector(
            maxBandsPerDirection: 10,
            new SearchPageResult([], 200),
            new SearchPageResult([listingA], 1),
            new SearchPageResult([listingB], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m1", "m2" }));
            Assert.That(summary.BandsFetched, Is.EqualTo(3));
            Assert.That(summary.TotalReported, Is.EqualTo(200));
            Assert.That(summary.CapHit, Is.False);
        });
    }

    [Test]
    public async Task Should_stop_fetching_bands_once_the_configured_cap_is_reached()
    {
        var collector = BuildCollector(maxBandsPerDirection: 5, new SearchPageResult([], 100));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(5));
            Assert.That(summary.CapHit, Is.True);
        });
    }

    [Test]
    public async Task Should_prune_only_the_band_that_yields_no_new_listings()
    {
        var known = new ListingSummary("m1", "Known", 1m, "USD", "https://x/m1", false, null, null, null);
        var collector = BuildCollector(maxBandsPerDirection: 10, new SearchPageResult([known], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: true, merged, new HashSet<string> { "m1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(1));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_only_prune_the_all_known_sibling_band_and_keep_walking_the_rest_of_the_queue()
    {
        var known = new ListingSummary("mKnown", "Known", 1m, "USD", "https://x/mKnown", false, null, null, null);
        var newInRightBand = new ListingSummary("mR0", "New", 1m, "USD", "https://x/mR0", false, null, null, null);
        var grandchildLeft = new ListingSummary("mG1", "New", 1m, "USD", "https://x/mG1", false, null, null, null);
        var grandchildRight = new ListingSummary("mG2", "New", 1m, "USD", "https://x/mG2", false, null, null, null);
        var collector = BuildCollector(
            maxBandsPerDirection: 10,
            new SearchPageResult([newInRightBand], 200),
            new SearchPageResult([known], 1),
            new SearchPageResult([newInRightBand], 150),
            new SearchPageResult([grandchildLeft], 1),
            new SearchPageResult([grandchildRight], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: true, merged, new HashSet<string> { "mKnown" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(5));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(1));
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "mKnown", "mR0", "mG1", "mG2" }));
        });
    }

    [Test]
    public async Task Should_not_stop_early_for_active_bands_even_when_listings_are_already_known()
    {
        var known = new ListingSummary("m1", "Known", 1m, "USD", "https://x/m1", false, null, null, null);
        var collector = BuildCollector(maxBandsPerDirection: 10, new SearchPageResult([known], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: false, merged, new HashSet<string> { "m1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(1));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(0));
            Assert.That(merged, Contains.Key("m1"));
        });
    }

    [Test]
    public async Task Should_not_treat_a_genuinely_empty_first_run_as_an_early_stop_for_known_listings()
    {
        var collector = BuildCollector(maxBandsPerDirection: 10, new SearchPageResult([], 0));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(1));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(0));
        });
    }

    private static MercariPriceBandCollector BuildCollector(int maxBandsPerDirection, params SearchPageResult[] results)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");

        var urls = Substitute.For<IPriceBandSearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Parse(Arg.Any<string>()).Returns(results[0], results[1..]);

        return new MercariPriceBandCollector(client, urls, parser, maxBandsPerDirection, NullLogger.Instance);
    }
}
