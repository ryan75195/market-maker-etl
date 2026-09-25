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

    private static IReadOnlyDictionary<string, string> EmptyWhere { get; } =
        new Dictionary<string, string>();

    private static PriceGroupQueryService BuildService(IReadOnlyList<PriceGroupListingCandidate> candidates)
    {
        var store = Substitute.For<IPriceGroupListingStore>();
        store.GetCandidates(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        return new PriceGroupQueryService(store, new FakeTimeProvider(Now));
    }

    private static PriceGroupListingCandidate BuildCandidate(
        int listingId,
        string colour,
        bool isSold,
        decimal? soldPrice = null,
        int? soldDaysAgo = null,
        decimal? price = null,
        bool isApplicable = true,
        bool needsReview = false,
        int taxonomyVersionId = 1) =>
        new(
            listingId,
            $"Listing {listingId}",
            $"https://example.test/{listingId}",
            "USD",
            isSold,
            price,
            soldPrice,
            soldDaysAgo.HasValue ? Now.UtcDateTime.AddDays(-soldDaysAgo.Value) : null,
            [new PriceGroupAnswer("colour", colour, isApplicable, needsReview, taxonomyVersionId)]);
}
