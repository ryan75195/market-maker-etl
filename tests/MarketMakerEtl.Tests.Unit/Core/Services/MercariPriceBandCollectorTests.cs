using System.Globalization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
            maxBandsPerDirection: 20,
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 200),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([listingA], 1),
            new SearchPageResult([listingB], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m1", "m2" }));
            Assert.That(summary.BandsFetched, Is.EqualTo(11));
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
    public async Task Should_prune_every_seed_band_that_yields_only_known_listings()
    {
        var known = new ListingSummary("m1", "Known", 1m, "USD", "https://x/m1", false, null, null, null);
        var collector = BuildCollector(maxBandsPerDirection: 20, new SearchPageResult([known], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: true, merged, new HashSet<string> { "m1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(9));
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m1" }));
        });
    }

    [Test]
    public async Task Should_only_prune_the_all_known_sibling_band_and_keep_walking_the_rest_of_the_queue()
    {
        var newInSeed = new ListingSummary("mSeed", "New", 1m, "USD", "https://x/mSeed", false, null, null, null);
        var known = new ListingSummary("mKnown", "Known", 1m, "USD", "https://x/mKnown", false, null, null, null);
        var newInRightBand = new ListingSummary("mR0", "New", 1m, "USD", "https://x/mR0", false, null, null, null);
        var grandchildLeft = new ListingSummary("mG1", "New", 1m, "USD", "https://x/mG1", false, null, null, null);
        var grandchildRight = new ListingSummary("mG2", "New", 1m, "USD", "https://x/mG2", false, null, null, null);
        var collector = BuildCollector(
            maxBandsPerDirection: 20,
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([newInSeed], 200),
            new SearchPageResult([known], 1),
            new SearchPageResult([newInRightBand], 150),
            new SearchPageResult([grandchildLeft], 1),
            new SearchPageResult([grandchildRight], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: true, merged, new HashSet<string> { "mKnown" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(13));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(9));
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "mSeed", "mKnown", "mR0", "mG1", "mG2" }));
        });
    }

    [Test]
    public async Task Should_not_stop_early_for_active_bands_even_when_listings_are_already_known()
    {
        var known = new ListingSummary("m1", "Known", 1m, "USD", "https://x/m1", false, null, null, null);
        var collector = BuildCollector(maxBandsPerDirection: 20, new SearchPageResult([known], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: false, merged, new HashSet<string> { "m1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(0));
            Assert.That(merged, Contains.Key("m1"));
        });
    }

    [Test]
    public async Task Should_force_sold_true_and_replace_a_stale_active_copy_when_merging_the_sold_direction()
    {
        var activeCopy = new ListingSummary("m1", "M1", 1m, "USD", "https://x/m1", false, null, null, null);
        var merged = new Dictionary<string, ListingSummary>();

        var activeCollector = BuildCollector(maxBandsPerDirection: 20, new SearchPageResult([activeCopy], 1));
        await activeCollector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(merged["m1"].IsSold, Is.False);

        var mistaggedSoldDirectionCopy = new ListingSummary("m1", "M1", 1m, "USD", "https://x/m1", false, null, null, null);
        var soldCollector = BuildCollector(maxBandsPerDirection: 20, new SearchPageResult([mistaggedSoldDirectionCopy], 1));
        await soldCollector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(merged["m1"].IsSold, Is.True);
    }

    [Test]
    public async Task Should_not_treat_a_genuinely_empty_first_run_as_an_early_stop_for_known_listings()
    {
        var collector = BuildCollector(maxBandsPerDirection: 20, new SearchPageResult([], 0));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_never_split_a_seed_band_that_reported_fewer_than_the_split_threshold()
    {
        var collector = BuildCollector(
            maxBandsPerDirection: 50,
            new SearchPageResult([], 10),
            new SearchPageResult([], 20),
            new SearchPageResult([], 30),
            new SearchPageResult([], 40),
            new SearchPageResult([], 50),
            new SearchPageResult([], 60),
            new SearchPageResult([], 70),
            new SearchPageResult([], 80),
            new SearchPageResult([], 90));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.CapHit, Is.False);
        });
    }

    [Test]
    public async Task Should_record_total_reported_as_the_sum_of_the_seed_band_counts()
    {
        var collector = BuildCollector(
            maxBandsPerDirection: 20,
            new SearchPageResult([], 5),
            new SearchPageResult([], 10),
            new SearchPageResult([], 15),
            new SearchPageResult([], 20),
            new SearchPageResult([], 25),
            new SearchPageResult([], 30),
            new SearchPageResult([], 35),
            new SearchPageResult([], 40),
            new SearchPageResult([], 45));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(summary.TotalReported, Is.EqualTo(225));
    }

    [Test]
    public async Task Should_prefer_splitting_the_band_with_the_highest_reported_count()
    {
        var dLow = new ListingSummary("dLow", "D", 1m, "USD", "https://x/dLow", false, null, null, null);
        var collector = BuildCollector(
            maxBandsPerDirection: 10,
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 150),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 300),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([dLow], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(10));
            Assert.That(summary.CapHit, Is.True);
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "dLow" }));
        });
    }

    [Test]
    public async Task Should_not_store_a_split_bands_own_items_when_backfilling()
    {
        var newest = BuildListing("s0", "https://x/s0");
        var oldest = BuildListing("s99", "https://x/s99");
        var page = new List<ListingSummary> { newest };
        for (var i = 1; i < 99; i++)
        {
            page.Add(BuildListing($"s{i}", $"https://x/s{i}"));
        }

        page.Add(oldest);

        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/s0"] = BuildDetail(daysAgo: 1),
            ["https://x/s99"] = BuildDetail(daysAgo: 1),
        };
        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 1,
            soldBackfillDays: 30,
            maxItemPageFetches: 10,
            detailsByUrl,
            new SearchPageResult(page, TotalCount: 150));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Is.Empty);
            Assert.That(summary.BackfillCutoffUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task Should_flag_the_backfill_budget_as_exhausted_when_item_page_fetches_run_out()
    {
        var newest = BuildListing("g0", "https://x/g0");
        var oldest = BuildListing("g1", "https://x/g1");
        var page = new List<ListingSummary> { newest, oldest };
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/g0"] = BuildDetail(daysAgo: 1),
        };
        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 1,
            soldBackfillDays: 30,
            maxItemPageFetches: 1,
            detailsByUrl,
            new SearchPageResult(page, TotalCount: 2));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(summary.BackfillBudgetExhausted, Is.True);
    }

    [Test]
    public async Task Should_use_the_existing_incremental_pruning_path_when_the_job_already_has_sold_listings()
    {
        var known = BuildListing("m1", "https://x/m1");
        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 20,
            soldBackfillDays: 30,
            maxItemPageFetches: 10,
            new Dictionary<string, ItemPageListing>(StringComparer.Ordinal),
            new SearchPageResult([known], 1));
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(
            SearchTerm, sold: true, merged, new HashSet<string> { "m1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.BandsPrunedForKnownListings, Is.EqualTo(9));
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m1" }));
            Assert.That(summary.BackfillCutoffUtc, Is.Null);
        });
    }

    [Test]
    public async Task Should_store_only_the_in_window_items_for_a_partial_band_even_with_local_order_inversions_near_the_boundary()
    {
        var offsetsInDays = new[] { 1, 5, 3, 7, 9, 11, 17, 19, 21, 23 };
        var page = new List<ListingSummary>();
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal);

        for (var i = 0; i < offsetsInDays.Length; i++)
        {
            var url = $"https://x/b{i}";
            page.Add(BuildListing($"b{i}", url));
            detailsByUrl[url] = BuildDetail(offsetsInDays[i]);
        }

        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 1,
            soldBackfillDays: 15,
            maxItemPageFetches: 20,
            detailsByUrl,
            new SearchPageResult(page, TotalCount: page.Count));
        var merged = new Dictionary<string, ListingSummary>();

        await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(
            merged.Keys,
            Is.EquivalentTo(new[] { "b0", "b1", "b2", "b3", "b4", "b5" }));
    }

    [Test]
    public async Task Should_not_lose_an_in_window_page_when_a_single_old_item_is_out_of_order_at_position_zero()
    {
        var outOfOrderOld = BuildListing("old0", "https://x/old0");
        var page = new List<ListingSummary> { outOfOrderOld };
        for (var i = 0; i < 9; i++)
        {
            page.Add(BuildListing($"n{i}", $"https://x/n{i}"));
        }

        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/old0"] = BuildDetail(daysAgo: 90),
        };

        for (var i = 0; i < 9; i++)
        {
            detailsByUrl[$"https://x/n{i}"] = BuildDetail(daysAgo: 1);
        }

        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 1,
            soldBackfillDays: 30,
            maxItemPageFetches: 20,
            detailsByUrl,
            new SearchPageResult(page, TotalCount: page.Count));
        var merged = new Dictionary<string, ListingSummary>();

        await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(merged.Keys, Is.SupersetOf(Enumerable.Range(0, 9).Select(i => $"n{i}")));
    }

    [Test]
    public async Task Should_still_treat_a_page_as_entirely_old_when_the_first_three_items_all_resolve_before_the_cutoff()
    {
        var page = new List<ListingSummary>
        {
            BuildListing("old0", "https://x/old0"),
            BuildListing("old1", "https://x/old1"),
            BuildListing("old2", "https://x/old2"),
            BuildListing("old3", "https://x/old3"),
        };
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/old0"] = BuildDetail(daysAgo: 90),
            ["https://x/old1"] = BuildDetail(daysAgo: 91),
            ["https://x/old2"] = BuildDetail(daysAgo: 92),
            ["https://x/old3"] = BuildDetail(daysAgo: 93),
        };

        var collector = BuildBackfillCollector(
            maxBandsPerDirection: 1,
            soldBackfillDays: 30,
            maxItemPageFetches: 20,
            detailsByUrl,
            new SearchPageResult(page, TotalCount: page.Count));
        var merged = new Dictionary<string, ListingSummary>();

        await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        Assert.That(merged, Is.Empty);
    }

    [Test]
    public async Task Should_collect_more_unique_listings_than_the_old_unfiltered_bisection_under_the_same_band_budget()
    {
        var catalogue = BuildCatalogueConcentratedBelowOneHundredDollars();
        const int bandBudget = 12;

        var geometricCollected = await CollectWithGeometricSeeding(catalogue, bandBudget);
        var bisectionCollected = await CollectWithUnfilteredBisectionBaseline(catalogue, bandBudget);

        Assert.That(
            geometricCollected,
            Is.GreaterThan(bisectionCollected),
            $"geometric={geometricCollected} bisection={bisectionCollected}");
    }

    [Test]
    public async Task Should_keep_collected_results_and_record_no_search_page_failure_when_a_fetch_transiently_fails_then_succeeds()
    {
        var listing = new ListingSummary("m1", "M1", 1m, "USD", "https://x/m1", false, null, null, null);
        var callCount = 0;
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new TimeoutException("Timeout 60000ms exceeded ... navigating to search page");
            }

            return Task.FromResult("<html/>");
        });

        var urls = Substitute.For<IPriceBandSearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Parse(Arg.Any<string>()).Returns(
            new SearchPageResult([listing], 1),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0));

        var settings = new MercariCollectionSettings(20, Backfill: null, SearchPageRetryDelay: TimeSpan.Zero);
        var collector = new MercariPriceBandCollector(client, urls, parser, settings, NullLogger.Instance);
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m1" }));
            Assert.That(summary.SearchPageFailures, Is.Null.Or.Empty);
        });
    }

    [Test]
    public async Task Should_store_other_bands_results_and_record_one_search_page_failure_when_a_band_persistently_fails_to_fetch()
    {
        var listing = new ListingSummary("m2", "M2", 1m, "USD", "https://x/m2", false, null, null, null);
        var callCount = 0;
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount <= 3)
            {
                throw new TimeoutException("Timeout 60000ms exceeded ... navigating to search page");
            }

            return Task.FromResult("<html/>");
        });

        var urls = Substitute.For<IPriceBandSearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Parse(Arg.Any<string>()).Returns(
            new SearchPageResult([listing], 1),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0),
            new SearchPageResult([], 0));

        var settings = new MercariCollectionSettings(20, Backfill: null, SearchPageRetryDelay: TimeSpan.Zero);
        var collector = new MercariPriceBandCollector(client, urls, parser, settings, NullLogger.Instance);
        var merged = new Dictionary<string, ListingSummary>();

        var summary = await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Keys, Is.EquivalentTo(new[] { "m2" }));
            Assert.That(summary.BandsFetched, Is.EqualTo(9));
            Assert.That(summary.SearchPageFailures, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Should_propagate_cancellation_instead_of_recording_a_search_page_failure()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new OperationCanceledException());

        var urls = Substitute.For<IPriceBandSearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();

        var settings = new MercariCollectionSettings(20, Backfill: null, SearchPageRetryDelay: TimeSpan.Zero);
        var collector = new MercariPriceBandCollector(client, urls, parser, settings, NullLogger.Instance);
        var merged = new Dictionary<string, ListingSummary>();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await collector.Collect(SearchTerm, sold: false, merged, new HashSet<string>(), CancellationToken.None));
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

        return new MercariPriceBandCollector(
            client, urls, parser, new MercariCollectionSettings(maxBandsPerDirection, Backfill: null), NullLogger.Instance);
    }

    private static MercariPriceBandCollector BuildBackfillCollector(
        int maxBandsPerDirection,
        int soldBackfillDays,
        int maxItemPageFetches,
        IReadOnlyDictionary<string, ItemPageListing> detailsByUrl,
        params SearchPageResult[] results)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (string)ci[0]);

        var urls = Substitute.For<IPriceBandSearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<decimal?>(), Arg.Any<decimal?>())
            .Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Parse(Arg.Any<string>()).Returns(results[0], results[1..]);

        var itemParser = Substitute.For<IItemPageParser>();
        itemParser.Parse(Arg.Any<string>())
            .Returns(ci => detailsByUrl.GetValueOrDefault((string)ci[0]));

        var backfill = new SoldBackfillPlanner(
            client, itemParser, soldBackfillDays, maxItemPageFetches, new FakeTimeProvider(DateTimeOffset.UtcNow));
        var settings = new MercariCollectionSettings(maxBandsPerDirection, backfill);
        return new MercariPriceBandCollector(client, urls, parser, settings, NullLogger.Instance);
    }

    private static ListingSummary BuildListing(string id, string url) =>
        new(id, id, 1m, "USD", url, false, null, null, null);

    private static ItemPageListing BuildDetail(int daysAgo) =>
        new(
            ListingId: null,
            Title: null,
            Price: null,
            Currency: null,
            Condition: null,
            BuyingFormat: null,
            Status: "Sold",
            SoldPrice: null,
            SoldDate: DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Seller: null,
            PrimaryImageUrl: null);

    private static async Task<int> CollectWithGeometricSeeding(IReadOnlyList<CatalogueItem> catalogue, int bandBudget)
    {
        var collector = new MercariPriceBandCollector(
            new PassthroughScrapeClient(),
            new CatalogueUrlService(),
            new CatalogueParser(catalogue),
            new MercariCollectionSettings(bandBudget, Backfill: null),
            NullLogger.Instance);
        var merged = new Dictionary<string, ListingSummary>();

        await collector.Collect(SearchTerm, sold: true, merged, new HashSet<string>(), CancellationToken.None);

        return merged.Count;
    }

    private static async Task<int> CollectWithUnfilteredBisectionBaseline(IReadOnlyList<CatalogueItem> catalogue, int bandBudget)
    {
        var urls = new CatalogueUrlService();
        var parser = new CatalogueParser(catalogue);
        var merged = new Dictionary<string, ListingSummary>();
        var bands = new Queue<PriceBand>();
        bands.Enqueue(PriceBand.Unfiltered);
        var fetched = 0;

        while (bands.Count > 0 && fetched < bandBudget)
        {
            var band = bands.Dequeue();
            var url = urls.BuildSearch(SearchTerm, sold: true, band.MinPrice, band.MaxPrice);
            var result = parser.Parse(url);
            fetched++;

            foreach (var listing in result.Listings)
            {
                merged[listing.ListingId] = listing;
            }

            var reportedCount = result.TotalCount ?? result.Listings.Count;
            if (reportedCount >= 100 && band.CanSplit(0.01m))
            {
                foreach (var child in band.Split())
                {
                    bands.Enqueue(child);
                }
            }
        }

        return await Task.FromResult(merged.Count);
    }

    private static IReadOnlyList<CatalogueItem> BuildCatalogueConcentratedBelowOneHundredDollars()
    {
        var items = new List<CatalogueItem>();
        AddCatalogueSegment(items, "seg0", 60, 0m, 5m);
        AddCatalogueSegment(items, "seg1", 40, 5.01m, 10m);
        AddCatalogueSegment(items, "seg2", 150, 10.01m, 20m);
        AddCatalogueSegment(items, "seg3", 300, 20.01m, 50m);
        AddCatalogueSegment(items, "seg4", 250, 50.01m, 100m);
        AddCatalogueSegment(items, "seg5", 30, 100.01m, 200m);
        AddCatalogueSegment(items, "seg6", 15, 200.01m, 500m);
        AddCatalogueSegment(items, "seg7", 5, 500.01m, 1000m);
        AddCatalogueSegment(items, "seg8", 20, 1000.01m, 40000m);
        return items;
    }

    private static void AddCatalogueSegment(List<CatalogueItem> items, string prefix, int count, decimal min, decimal max)
    {
        for (var i = 0; i < count; i++)
        {
            var fraction = count == 1 ? 0m : (decimal)i / (count - 1);
            var price = Math.Round(min + ((max - min) * fraction), 2);
            items.Add(new CatalogueItem($"{prefix}-{i}", price));
        }
    }

    private sealed record CatalogueItem(string Id, decimal Price);

    private sealed record DecodedRange(decimal? Min, decimal? Max);

    private sealed class PassthroughScrapeClient : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) => Task.FromResult(url);
    }

    private sealed class CatalogueUrlService : IPriceBandSearchUrlService
    {
        public string BuildSearch(string searchTerm, bool sold, decimal? minPrice, decimal? maxPrice) =>
            $"catalogue://{Encode(minPrice)}/{Encode(maxPrice)}";

        private static string Encode(decimal? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "open";
    }

    private sealed class CatalogueParser : ISearchPageParser
    {
        private readonly IReadOnlyList<CatalogueItem> _items;

        public CatalogueParser(IReadOnlyList<CatalogueItem> items) => _items = items;

        public Marketplace Marketplace => Marketplace.Mercari;

        public bool ContainsListingMarkup(string html) => true;

        public SearchPageResult Parse(string html)
        {
            var range = Decode(html);
            var matches = _items
                .Where(item => (range.Min is null || item.Price >= range.Min)
                    && (range.Max is null || item.Price <= range.Max))
                .OrderBy(item => item.Price)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();

            var page = matches
                .Take(100)
                .Select(item => new ListingSummary(
                    item.Id, item.Id, item.Price, "USD", $"https://x/{item.Id}", false, null, null, null))
                .ToList();

            return new SearchPageResult(page, matches.Count);
        }

        private static DecodedRange Decode(string html)
        {
            var parts = html.Replace("catalogue://", string.Empty, StringComparison.Ordinal).Split('/');
            return new DecodedRange(DecodeValue(parts[0]), DecodeValue(parts[1]));
        }

        private static decimal? DecodeValue(string value) =>
            value == "open" ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
