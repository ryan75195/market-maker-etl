using Microsoft.Extensions.Time.Testing;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PriceGroupQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Should_group_listings_by_the_requested_question_and_count_sold_listings_per_group()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 5),
            BuildCandidate(2, "white", isSold: true, soldPrice: 120m, soldDaysAgo: 10),
            BuildCandidate(3, "black", isSold: true, soldPrice: 200m, soldDaysAgo: 2)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].Key["colour"], Is.EqualTo("white"));
            Assert.That(groups[0].SoldCount, Is.EqualTo(2));
            Assert.That(groups[0].SoldMedian, Is.EqualTo(110m));
            Assert.That(groups[1].Key["colour"], Is.EqualTo("black"));
            Assert.That(groups[1].SoldCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_exclude_a_needs_review_answer_by_default_and_include_it_with_include_uncertain()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, needsReview: true)
        };
        var service = BuildService(candidates);

        var excluded = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);
        var included = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, true, 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(excluded, Is.Empty);
            Assert.That(included, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_skip_a_listing_when_a_group_by_question_is_not_applicable()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, isApplicable: false)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public async Task Should_exclude_an_answer_recorded_under_an_older_taxonomy_version()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, taxonomyVersionId: 1)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(2, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public async Task Should_only_count_sold_listings_within_the_sold_days_window()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 3),
            BuildCandidate(2, "white", isSold: true, soldPrice: 500m, soldDaysAgo: 10)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 7, false, 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].SoldCount, Is.EqualTo(1));
            Assert.That(groups[0].SoldMedian, Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task Should_use_updated_utc_as_the_sold_date_when_sold_date_is_missing()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, effectiveSoldDaysAgo: 3),
            BuildCandidate(2, "white", isSold: true, soldPrice: 500m, effectiveSoldDaysAgo: 60)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].SoldCount, Is.EqualTo(1));
            Assert.That(groups[0].SoldMedian, Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task Should_mark_sold_date_as_estimated_when_the_real_sold_date_is_missing()
    {
        var where = new Dictionary<string, string> { ["colour"] = "white" };
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1),
            BuildCandidate(2, "white", isSold: true, soldPrice: 120m, effectiveSoldDaysAgo: 2)
        };
        var service = BuildService(candidates);

        var listings = await service.GetGroupListings(
            new PriceGroupListingsQuery(1, where, PriceGroupListingStatus.Sold, 50, 30, false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            var withRealDate = listings.Single(l => l.ListingId == 1);
            var withEstimatedDate = listings.Single(l => l.ListingId == 2);
            Assert.That(withRealDate.SoldDateIsEstimated, Is.False);
            Assert.That(withEstimatedDate.SoldDate, Is.Null);
            Assert.That(withEstimatedDate.SoldDateIsEstimated, Is.True);
        });
    }

    [Test]
    public async Task Should_hide_groups_with_fewer_sold_listings_than_min_sold()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 2), CancellationToken.None);

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public async Task Should_order_active_listings_by_delta_from_sold_median_ascending()
    {
        var where = new Dictionary<string, string> { ["colour"] = "white" };
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1),
            BuildCandidate(2, "white", isSold: false, price: 150m),
            BuildCandidate(3, "white", isSold: false, price: 90m)
        };
        var service = BuildService(candidates);

        var listings = await service.GetGroupListings(
            new PriceGroupListingsQuery(1, where, PriceGroupListingStatus.Active, 50, 30, false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(2));
            Assert.That(listings[0].ListingId, Is.EqualTo(3));
            Assert.That(listings[0].DeltaFromSoldMedian, Is.EqualTo(-10m));
            Assert.That(listings[1].ListingId, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_group_listings_by_mercari_condition_answers()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "good", isSold: true, soldPrice: 100m, soldDaysAgo: 1, question: "mercari_condition"),
            BuildCandidate(2, "poor", isSold: true, soldPrice: 20m, soldDaysAgo: 1, question: "mercari_condition")
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["mercari_condition"], 30, false, 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups.Select(g => g.Key["mercari_condition"]), Is.EquivalentTo(["good", "poor"]));
        });
    }

    [Test]
    public async Task Should_compute_sold_net_and_active_landed_stats_honouring_each_shipping_payer_case()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, shippingPayer: "seller", shippingCost: 10m),
            BuildCandidate(2, "white", isSold: true, soldPrice: 200m, soldDaysAgo: 1, shippingPayer: "buyer", shippingCost: 15m),
            BuildCandidate(3, "white", isSold: true, soldPrice: 150m, soldDaysAgo: 1),
            BuildCandidate(4, "white", isSold: false, price: 50m, shippingPayer: "buyer", shippingCost: 5m),
            BuildCandidate(5, "white", isSold: false, price: 80m, shippingPayer: "seller", shippingCost: 5m),
            BuildCandidate(6, "white", isSold: false, price: 60m)
        };
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        var group = groups.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.SoldNetMedian, Is.EqualTo(134.5m));
            Assert.That(group.SoldNetP25, Is.EqualTo(107.0m));
            Assert.That(group.SoldNetP75, Is.EqualTo(157.0m));
            Assert.That(group.ActiveLandedMedian, Is.EqualTo(60m));
            Assert.That(group.ActiveLandedMin, Is.EqualTo(55m));
            Assert.That(group.ShippingUnknownCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_honour_the_configured_fee_rate_and_fixed_fee_in_sold_net_median()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, shippingPayer: "seller", shippingCost: 10m)
        };
        var service = BuildService(candidates, new PriceGroupOptions(0.20m, 1.00m));

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);

        Assert.That(groups.Single().SoldNetMedian, Is.EqualTo(69m));
    }

    [Test]
    public async Task Should_not_trim_sold_prices_when_the_group_is_below_the_iqr_threshold()
    {
        var soldPrices = new decimal[] { 100m, 101m, 99m, 102m, 98m, 103m, 500m };
        var candidates = soldPrices
            .Select((price, index) => BuildCandidate(index + 1, "white", isSold: true, soldPrice: price, soldDaysAgo: 1))
            .ToList();
        var service = BuildService(candidates);

        var groups = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1, TrimIqr: true), CancellationToken.None);

        var group = groups.Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.SoldCount, Is.EqualTo(7));
            Assert.That(group.TrimmedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_trim_outlier_sold_prices_at_the_iqr_threshold_and_report_trimmed_count()
    {
        var soldPrices = new decimal[] { 100m, 101m, 99m, 102m, 98m, 103m, 97m, 500m };
        var candidates = soldPrices
            .Select((price, index) => BuildCandidate(index + 1, "white", isSold: true, soldPrice: price, soldDaysAgo: 1))
            .ToList();
        var service = BuildService(candidates);

        var untrimmed = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1), CancellationToken.None);
        var trimmed = await service.GetPriceGroups(
            new PriceGroupQuery(1, EmptyWhere, ["colour"], 30, false, 1, TrimIqr: true), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(untrimmed.Single().SoldCount, Is.EqualTo(8));
            Assert.That(untrimmed.Single().TrimmedCount, Is.EqualTo(0));
            Assert.That(trimmed.Single().SoldCount, Is.EqualTo(7));
            Assert.That(trimmed.Single().TrimmedCount, Is.EqualTo(1));
            Assert.That(trimmed.Single().SoldMax, Is.EqualTo(103m));
        });
    }

    [Test]
    public async Task Should_include_landed_price_net_proceeds_and_delta_in_group_listings()
    {
        var where = new Dictionary<string, string> { ["colour"] = "white" };
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1, shippingPayer: "seller", shippingCost: 10m),
            BuildCandidate(2, "white", isSold: false, price: 150m, shippingPayer: "buyer", shippingCost: 20m),
            BuildCandidate(3, "white", isSold: false, price: 90m)
        };
        var service = BuildService(candidates);

        var listings = await service.GetGroupListings(
            new PriceGroupListingsQuery(1, where, PriceGroupListingStatus.Active, 50, 30, false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            var near = listings.Single(l => l.ListingId == 3);
            var far = listings.Single(l => l.ListingId == 2);
            Assert.That(near.LandedPrice, Is.EqualTo(90m));
            Assert.That(near.NetProceeds, Is.EqualTo(80.5m));
            Assert.That(near.DeltaLandedFromSoldNetMedian, Is.EqualTo(10.5m));
            Assert.That(far.LandedPrice, Is.EqualTo(170m));
            Assert.That(far.NetProceeds, Is.EqualTo(134.5m));
            Assert.That(far.DeltaLandedFromSoldNetMedian, Is.EqualTo(90.5m));
        });
    }

    [Test]
    public async Task Should_bucket_sold_price_history_by_week_including_empty_buckets()
    {
        var where = new Dictionary<string, string> { ["colour"] = "white" };
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1),
            BuildCandidate(2, "white", isSold: true, soldPrice: 120m, soldDaysAgo: 2)
        };
        var service = BuildService(candidates);

        var buckets = await service.GetPriceGroupHistory(
            new PriceGroupHistoryQuery(
                1, where, PriceGroupHistoryBucketGranularity.Week, 3, PriceGroupHistoryBasis.Listed, false, false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(buckets, Has.Count.EqualTo(3));
            Assert.That(buckets.Sum(b => b.SoldCount), Is.EqualTo(2));
            Assert.That(buckets.Any(b => b.SoldCount == 0), Is.True);
        });
    }

    [Test]
    public async Task Should_only_count_sales_strictly_after_the_window_start_and_up_to_the_window_end()
    {
        var groupKey = new Dictionary<string, string> { ["colour"] = "white" };
        var windowStart = Now.UtcDateTime;
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidateAt(1, "white", windowStart),
            BuildCandidateAt(2, "white", windowStart.AddDays(5)),
            BuildCandidateAt(3, "white", windowStart.AddDays(14)),
            BuildCandidateAt(4, "white", windowStart.AddDays(15))
        };
        var service = BuildService(candidates);

        var result = await service.GetForwardWindowStats(
            new PriceGroupForwardWindowQuery(1, groupKey, windowStart, windowStart.AddDays(14), false),
            CancellationToken.None);

        Assert.That(result.SoldCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_flag_used_estimated_dates_when_a_sale_in_the_window_has_no_real_sold_date()
    {
        var groupKey = new Dictionary<string, string> { ["colour"] = "white" };
        var windowStart = Now.UtcDateTime;
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidateAt(1, "white", windowStart.AddDays(3), soldDateIsEstimated: true)
        };
        var service = BuildService(candidates);

        var result = await service.GetForwardWindowStats(
            new PriceGroupForwardWindowQuery(1, groupKey, windowStart, windowStart.AddDays(14), false),
            CancellationToken.None);

        Assert.That(result.UsedEstimatedDates, Is.True);
    }

    [Test]
    public async Task Should_compute_the_net_median_of_sales_within_the_forward_window()
    {
        var groupKey = new Dictionary<string, string> { ["colour"] = "white" };
        var windowStart = Now.UtcDateTime;
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidateAt(1, "white", windowStart.AddDays(1), soldPrice: 90m),
            BuildCandidateAt(2, "white", windowStart.AddDays(2), soldPrice: 100m),
            BuildCandidateAt(3, "white", windowStart.AddDays(3), soldPrice: 110m)
        };
        var service = BuildService(candidates, new PriceGroupOptions(0m, 0m));

        var result = await service.GetForwardWindowStats(
            new PriceGroupForwardWindowQuery(1, groupKey, windowStart, windowStart.AddDays(14), false),
            CancellationToken.None);

        Assert.That(result.NetMedian, Is.EqualTo(100m));
    }

    private static PriceGroupListingCandidate BuildCandidateAt(
        int listingId,
        string colour,
        DateTime soldAtUtc,
        decimal soldPrice = 100m,
        bool soldDateIsEstimated = false) =>
        new(
            listingId,
            $"Listing {listingId}",
            $"https://example.test/{listingId}",
            "USD",
            true,
            null,
            soldPrice,
            soldDateIsEstimated ? null : soldAtUtc,
            soldAtUtc,
            [new PriceGroupAnswer("colour", colour, true, false, 1)]);

    private static IReadOnlyDictionary<string, string> EmptyWhere { get; } =
        new Dictionary<string, string>();

    private static readonly PriceGroupOptions DefaultOptions = new(0.10m, 0.50m);

    private static PriceGroupQueryService BuildService(IReadOnlyList<PriceGroupListingCandidate> candidates) =>
        BuildService(candidates, DefaultOptions);

    private static PriceGroupQueryService BuildService(
        IReadOnlyList<PriceGroupListingCandidate> candidates, PriceGroupOptions options)
    {
        var store = Substitute.For<IPriceGroupListingStore>();
        store.GetCandidates(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        return new PriceGroupQueryService(store, new FakeTimeProvider(Now), options);
    }

    private static PriceGroupListingCandidate BuildCandidate(
        int listingId,
        string colour,
        bool isSold,
        decimal? soldPrice = null,
        int? soldDaysAgo = null,
        int? effectiveSoldDaysAgo = null,
        decimal? price = null,
        bool isApplicable = true,
        bool needsReview = false,
        int taxonomyVersionId = 1,
        string question = "colour",
        string? shippingPayer = null,
        decimal? shippingCost = null)
    {
        var soldDate = soldDaysAgo.HasValue ? Now.UtcDateTime.AddDays(-soldDaysAgo.Value) : (DateTime?)null;
        var effectiveSoldDate = Now.UtcDateTime.AddDays(-(effectiveSoldDaysAgo ?? soldDaysAgo ?? 0));
        return new(
            listingId,
            $"Listing {listingId}",
            $"https://example.test/{listingId}",
            "USD",
            isSold,
            price,
            soldPrice,
            soldDate,
            effectiveSoldDate,
            [new PriceGroupAnswer(question, colour, isApplicable, needsReview, taxonomyVersionId)],
            shippingPayer,
            shippingCost);
    }
}
