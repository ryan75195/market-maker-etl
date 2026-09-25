using System.Globalization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldBackfillPlannerTests
{
    private const int SoldBackfillDays = 30;
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Should_report_the_full_page_as_in_window_when_the_oldest_item_has_no_usable_date()
    {
        var newest = BuildListing("n0", "https://x/n0");
        var oldest = BuildListing("o0", string.Empty);
        var page = new SearchPageResult([newest, oldest], TotalCount: 2);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/n0"] = BuildDetail(daysAgo: 1),
        };
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(2, overflowed: false)));
    }

    [Test]
    public async Task Should_split_a_full_band_whose_last_item_is_still_inside_the_window()
    {
        var page = BuildFullPage(200);
        var detailsByUrl = FullPageDetails(page, oldestDaysAgo: 1);
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision.Kind, Is.EqualTo(SoldBackfillOutcomeKind.Split));
    }

    [Test]
    public async Task Should_flag_overflow_when_a_full_in_window_band_cannot_split_further()
    {
        var page = BuildFullPage(200);
        var detailsByUrl = FullPageDetails(page, oldestDaysAgo: 1);
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: false, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(100, overflowed: true)));
    }

    [Test]
    public async Task Should_find_the_cutoff_tolerant_of_a_few_out_of_order_dates_near_the_boundary()
    {
        var offsetsInDays = new[] { 1, 5, 3, 7, 9, 11, 17, 19, 21, 23 };
        var listings = new List<ListingSummary>();
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal);

        for (var i = 0; i < offsetsInDays.Length; i++)
        {
            var url = $"https://x/b{i}";
            listings.Add(BuildListing($"b{i}", url));
            detailsByUrl[url] = BuildDetail(offsetsInDays[i]);
        }

        var page = new SearchPageResult(listings, TotalCount: listings.Count);
        var planner = BuildPlanner(detailsByUrl, soldBackfillDays: 15);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(6, overflowed: false)));
    }

    [Test]
    public async Task Should_treat_a_trading_item_with_no_sold_date_as_sold_now()
    {
        var listing = BuildListing("t0", "https://x/t0");
        var page = new SearchPageResult([listing], TotalCount: 1);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/t0"] = new(
                ListingId: null,
                Title: null,
                Price: null,
                Currency: null,
                Condition: null,
                BuyingFormat: null,
                Status: "Sold",
                SoldPrice: null,
                SoldDate: null,
                Seller: null,
                PrimaryImageUrl: null),
        };
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(1, overflowed: false)));
    }

    [Test]
    public async Task Should_report_nothing_in_window_when_the_newest_item_is_already_older_than_the_cutoff()
    {
        var newest = BuildListing("n0", "https://x/n0");
        var page = new SearchPageResult([newest], TotalCount: 1);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/n0"] = BuildDetail(daysAgo: 90),
        };
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.None()));
    }

    [Test]
    public async Task Should_not_discard_an_in_window_page_because_a_single_out_of_order_old_item_sits_at_position_zero()
    {
        var outOfOrderOld = BuildListing("old0", "https://x/old0");
        var listings = new List<ListingSummary> { outOfOrderOld };
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/old0"] = BuildDetail(daysAgo: 90),
        };

        for (var i = 0; i < 9; i++)
        {
            var url = $"https://x/n{i}";
            listings.Add(BuildListing($"n{i}", url));
            detailsByUrl[url] = BuildDetail(daysAgo: 1);
        }

        var page = new SearchPageResult(listings, TotalCount: listings.Count);
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision.Kind, Is.EqualTo(SoldBackfillOutcomeKind.Store));
        Assert.That(decision.StoreCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task Should_still_report_none_when_the_first_three_items_all_resolve_before_the_cutoff()
    {
        var listings = new List<ListingSummary>
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
        var page = new SearchPageResult(listings, TotalCount: listings.Count);
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SoldBackfillDecision.None()));
            Assert.That(planner.ItemPageFetchesUsed, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Should_store_only_what_is_already_decided_when_the_item_page_fetch_budget_is_exhausted()
    {
        var newest = BuildListing("n0", "https://x/n0");
        var middle = BuildListing("n1", "https://x/n1");
        var oldest = BuildListing("n2", "https://x/n2");
        var page = new SearchPageResult([newest, middle, oldest], TotalCount: 3);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/n0"] = BuildDetail(daysAgo: 1),
            ["https://x/n2"] = BuildDetail(daysAgo: 90),
        };
        var planner = BuildPlanner(detailsByUrl, maxItemPageFetches: 1);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(1, overflowed: false)));
            Assert.That(planner.ItemPageFetchesUsed, Is.EqualTo(1));
            Assert.That(planner.BudgetExhausted, Is.True);
        });
    }

    [Test]
    public async Task Should_resolve_the_date_after_the_item_page_fetch_fails_once_then_succeeds()
    {
        var listing = BuildListing("r0", "https://x/r0");
        var page = new SearchPageResult([listing], TotalCount: 1);
        var attempts = 0;

        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml("https://x/r0", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("transient scrape failure");
                }

                return "https://x/r0";
            });

        var itemParser = Substitute.For<IItemPageParser>();
        itemParser.Parse("https://x/r0").Returns(BuildDetail(daysAgo: 1));

        var planner = new SoldBackfillPlanner(
            client, itemParser, SoldBackfillDays, 100, new FakeTimeProvider(Now));

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(1, overflowed: false)));
            Assert.That(planner.ItemPageFetchesUsed, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_treat_a_timed_out_item_page_fetch_as_unresolved_rather_than_throw()
    {
        var listing = BuildListing("t0", "https://x/t0");
        var page = new SearchPageResult([listing], TotalCount: 1);

        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml("https://x/t0", Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TimeoutException("Fetching https://x/t0 timed out after 00:00:05."));

        var itemParser = Substitute.For<IItemPageParser>();
        var planner = new SoldBackfillPlanner(client, itemParser, SoldBackfillDays, 100, new FakeTimeProvider(Now));

        SoldBackfillDecision decision = default;
        Assert.DoesNotThrowAsync(async () => decision = await planner.Resolve(page, canSplit: true, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(0, overflowed: false)));
            Assert.That(planner.ResolvedDetails, Does.Not.ContainKey("t0"));
            Assert.That(planner.BudgetExhausted, Is.False);
        });
    }

    [Test]
    public async Task Should_not_let_an_unresolvable_midpoint_push_the_cutoff_later()
    {
        var n0 = BuildListing("u0", "https://x/u0");
        var n1 = BuildListing("u1", "https://x/u1");
        var n2 = BuildListing("u2", "https://x/u2");
        var n3 = BuildListing("u3", "https://x/u3");
        var page = new SearchPageResult([n0, n1, n2, n3], TotalCount: 4);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/u0"] = BuildDetail(daysAgo: 1),
            ["https://x/u2"] = BuildDetail(daysAgo: 40),
            ["https://x/u3"] = BuildDetail(daysAgo: 45),
        };
        var planner = BuildPlanner(detailsByUrl);

        var decision = await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(decision, Is.EqualTo(SoldBackfillDecision.Store(1, overflowed: false)));
    }

    [Test]
    public async Task Should_retain_the_parsed_detail_for_every_successfully_resolved_listing()
    {
        var newest = BuildListing("n0", "https://x/n0");
        var oldest = BuildListing("o0", string.Empty);
        var page = new SearchPageResult([newest, oldest], TotalCount: 2);
        var detail = BuildDetail(daysAgo: 1);
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["https://x/n0"] = detail,
        };
        var planner = BuildPlanner(detailsByUrl);

        await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(planner.ResolvedDetails, Contains.Key("n0"));
            Assert.That(planner.ResolvedDetails["n0"], Is.EqualTo(detail));
            Assert.That(planner.ResolvedDetails, Does.Not.ContainKey("o0"));
        });
    }

    [Test]
    public async Task Should_not_retain_a_detail_for_a_listing_whose_page_never_parses()
    {
        var listing = BuildListing("unparseable0", "https://x/unparseable0");
        var page = new SearchPageResult([listing], TotalCount: 1);
        var planner = BuildPlanner(new Dictionary<string, ItemPageListing>(StringComparer.Ordinal));

        await planner.Resolve(page, canSplit: true, CancellationToken.None);

        Assert.That(planner.ResolvedDetails, Does.Not.ContainKey("unparseable0"));
    }

    [Test]
    public void Should_compute_the_cutoff_from_the_injected_time_provider()
    {
        var client = Substitute.For<IScrapeClient>();
        var itemParser = Substitute.For<IItemPageParser>();
        var timeProvider = new FakeTimeProvider(Now);

        var planner = new SoldBackfillPlanner(client, itemParser, SoldBackfillDays, 100, timeProvider);

        Assert.That(planner.CutoffUtc, Is.EqualTo(Now.UtcDateTime.AddDays(-SoldBackfillDays)));
    }

    private static SoldBackfillPlanner BuildPlanner(
        IReadOnlyDictionary<string, ItemPageListing> detailsByUrl,
        int soldBackfillDays = SoldBackfillDays,
        int maxItemPageFetches = 100)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (string)ci[0]);

        var itemParser = Substitute.For<IItemPageParser>();
        itemParser.Parse(Arg.Any<string>())
            .Returns(ci => detailsByUrl.GetValueOrDefault((string)ci[0]));

        return new SoldBackfillPlanner(
            client, itemParser, soldBackfillDays, maxItemPageFetches, new FakeTimeProvider(Now));
    }

    private static SearchPageResult BuildFullPage(int totalCount)
    {
        var listings = new List<ListingSummary>();
        for (var i = 0; i < 100; i++)
        {
            listings.Add(BuildListing($"f{i}", $"https://x/f{i}"));
        }

        return new SearchPageResult(listings, TotalCount: totalCount);
    }

    private static Dictionary<string, ItemPageListing> FullPageDetails(SearchPageResult page, int oldestDaysAgo)
    {
        var detailsByUrl = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            [page.Listings[0].Url!] = BuildDetail(daysAgo: 1),
            [page.Listings[^1].Url!] = BuildDetail(oldestDaysAgo),
        };
        return detailsByUrl;
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
            SoldDate: Now.UtcDateTime.AddDays(-daysAgo).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Seller: null,
            PrimaryImageUrl: null);
}
