using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PriceGroupHistoryCalculatorTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MondayOfCurrentWeek = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MondayOfPreviousWeek = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly PriceGroupOptions Options = new(0.10m, 0.50m);

    [Test]
    public void Should_align_week_buckets_to_utc_monday_and_place_a_sunday_sale_in_the_prior_week()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, soldPrice: 100m, soldDate: MondayOfCurrentWeek),
            BuildCandidate(2, soldPrice: 200m, soldDate: MondayOfPreviousWeek.AddDays(6).AddHours(23))
        };
        var query = BuildQuery(PriceGroupHistoryBucketGranularity.Week, weeks: 2);

        var buckets = PriceGroupHistoryCalculator.Build(candidates, query, NowUtc, Options);

        Assert.Multiple(() =>
        {
            Assert.That(buckets, Has.Count.EqualTo(2));
            Assert.That(buckets[0].BucketStart, Is.EqualTo(MondayOfPreviousWeek));
            Assert.That(buckets[0].SoldCount, Is.EqualTo(1));
            Assert.That(buckets[0].Median, Is.EqualTo(200m));
            Assert.That(buckets[1].BucketStart, Is.EqualTo(MondayOfCurrentWeek));
            Assert.That(buckets[1].SoldCount, Is.EqualTo(1));
            Assert.That(buckets[1].Median, Is.EqualTo(100m));
        });
    }

    [Test]
    public void Should_exclude_estimated_sold_dates_when_include_estimated_dates_is_false()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, soldPrice: 100m, soldDate: MondayOfCurrentWeek),
            BuildCandidate(2, soldPrice: 300m, soldDate: null, effectiveSoldDate: MondayOfCurrentWeek)
        };
        var excludingEstimated = BuildQuery(PriceGroupHistoryBucketGranularity.Week, weeks: 1, includeEstimatedDates: false);
        var includingEstimated = BuildQuery(PriceGroupHistoryBucketGranularity.Week, weeks: 1, includeEstimatedDates: true);

        var withoutEstimated = PriceGroupHistoryCalculator.Build(candidates, excludingEstimated, NowUtc, Options);
        var withEstimated = PriceGroupHistoryCalculator.Build(candidates, includingEstimated, NowUtc, Options);

        Assert.Multiple(() =>
        {
            Assert.That(withoutEstimated.Single().SoldCount, Is.EqualTo(1));
            Assert.That(withEstimated.Single().SoldCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Should_include_empty_buckets_with_a_zero_count_and_no_percentiles()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, soldPrice: 100m, soldDate: MondayOfCurrentWeek)
        };
        var query = BuildQuery(PriceGroupHistoryBucketGranularity.Week, weeks: 4);

        var buckets = PriceGroupHistoryCalculator.Build(candidates, query, NowUtc, Options);

        Assert.Multiple(() =>
        {
            Assert.That(buckets, Has.Count.EqualTo(4));
            var emptyBuckets = buckets.Where(b => b.BucketStart != MondayOfCurrentWeek).ToList();
            Assert.That(emptyBuckets, Has.Count.EqualTo(3));
            Assert.That(emptyBuckets, Has.All.Matches<PriceGroupHistoryBucket>(b => b.SoldCount == 0));
            Assert.That(emptyBuckets, Has.All.Matches<PriceGroupHistoryBucket>(b => b.Median == null));
        });
    }

    [Test]
    public void Should_compute_percentiles_from_net_proceeds_when_basis_is_net()
    {
        var candidates = new List<PriceGroupListingCandidate>
        {
            BuildCandidate(1, soldPrice: 100m, soldDate: MondayOfCurrentWeek, shippingPayer: "seller", shippingCost: 10m)
        };
        var query = BuildQuery(PriceGroupHistoryBucketGranularity.Week, weeks: 1, basis: PriceGroupHistoryBasis.Net);

        var bucket = PriceGroupHistoryCalculator.Build(candidates, query, NowUtc, Options).Single();

        Assert.That(bucket.Median, Is.EqualTo(79.5m));
    }

    private static PriceGroupHistoryQuery BuildQuery(
        PriceGroupHistoryBucketGranularity bucket,
        int weeks,
        PriceGroupHistoryBasis basis = PriceGroupHistoryBasis.Listed,
        bool includeEstimatedDates = false,
        bool trimIqr = false) =>
        new(1, new Dictionary<string, string>(), bucket, weeks, basis, includeEstimatedDates, trimIqr);

    private static PriceGroupListingCandidate BuildCandidate(
        int listingId,
        decimal soldPrice,
        DateTime? soldDate,
        DateTime? effectiveSoldDate = null,
        string? shippingPayer = null,
        decimal? shippingCost = null) =>
        new(
            listingId,
            $"Listing {listingId}",
            $"https://example.test/{listingId}",
            "USD",
            true,
            null,
            soldPrice,
            soldDate,
            effectiveSoldDate ?? soldDate ?? MondayOfCurrentWeek,
            [],
            shippingPayer,
            shippingCost);
}
