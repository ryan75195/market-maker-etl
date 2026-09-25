using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ItemDetailFetchServiceTests
{
    private const int JobId = 7;

    private static readonly ListingDetailTarget Target =
        new(1, "listing-1", "https://x/itm/1", "Active", Marketplace.Mercari);

    [Test]
    public async Task Should_apply_detail_for_a_target_whose_page_parses_successfully()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        var page = BuildPage();
        var parser = BuildParser(Marketplace.Mercari, page);
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        await store.Received(1).ApplyItemDetail(Target.Id, page, Arg.Any<CancellationToken>());
        await store.DidNotReceive().MarkDetailFetchFailed(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_mark_the_listing_failed_when_the_page_fails_to_parse()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        var parser = BuildParser(Marketplace.Mercari, page: null);
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        await store.Received(1).MarkDetailFetchFailed(Target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ApplyItemDetail(Arg.Any<int>(), Arg.Any<ItemPageListing>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_mark_the_listing_failed_and_continue_when_the_client_throws()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("blocked"));
        var parser = BuildParser(Marketplace.Mercari, BuildPage());
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        Assert.DoesNotThrowAsync(() => service.FetchDetails(JobId, CancellationToken.None));
        await store.Received(1).MarkDetailFetchFailed(Target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_mark_the_listing_failed_and_continue_when_the_fetch_times_out()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TimeoutException($"Fetching {Target.Url} timed out after 00:00:05."));
        var parser = BuildParser(Marketplace.Mercari, BuildPage());
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        var issues = await service.FetchDetails(JobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].ErrorMessage, Does.Contain("timed out"));
        });
        await store.Received(1).MarkDetailFetchFailed(Target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_record_the_innermost_exception_message_and_type_when_the_save_fails()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        var parser = BuildParser(Marketplace.Mercari, BuildPage());
        var innermost = new Microsoft.Data.Sqlite.SqliteException("SQLite Error 5: 'database is locked'.", 5);
        var outer = new DbUpdateException("An error occurred while saving the entity changes.", innermost);
        store.ApplyItemDetail(Target.Id, Arg.Any<ItemPageListing>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw outer);
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        var issues = await service.FetchDetails(JobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].ErrorMessage, Does.Contain("SQLite Error 5: 'database is locked'."));
            Assert.That(issues[0].ErrorMessage, Does.Contain(nameof(Microsoft.Data.Sqlite.SqliteException)));
        });
    }

    [Test]
    public async Task Should_mark_the_listing_failed_when_no_parser_matches_the_marketplace()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Target]);
        var client = Substitute.For<IScrapeClient>();
        var parser = BuildParser(Marketplace.Ebay, BuildPage());
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        await store.Received(1).MarkDetailFetchFailed(Target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_do_nothing_when_no_listings_need_detail()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var client = Substitute.For<IScrapeClient>();
        var service = new ItemDetailFetchService(store, client, [], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        await client.DidNotReceive().GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ApplyItemDetail(Arg.Any<int>(), Arg.Any<ItemPageListing>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_pass_the_per_run_cap_as_the_fetch_limit()
    {
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var options = new DetailFetchOptions(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 17, MaxDetailFetchAttempts: 3);
        var service = new ItemDetailFetchService(store, Substitute.For<IScrapeClient>(), [], options, NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        await store.Received(1).GetListingsNeedingDetail(JobId, 17, 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_never_exceed_the_configured_concurrency_limit_while_fetching_details()
    {
        var targets = Enumerable.Range(1, 8)
            .Select(i => new ListingDetailTarget(i, $"listing-{i}", $"https://x/itm/{i}", "Active", Marketplace.Mercari))
            .ToArray();
        var store = Substitute.For<IItemDetailStore>();
        store.GetListingsNeedingDetail(JobId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(targets);
        var client = new ConcurrencyTrackingScrapeClient(TimeSpan.FromMilliseconds(150));
        var parser = BuildParser(Marketplace.Mercari, BuildPage());
        var options = new DetailFetchOptions(MaxConcurrentDetailFetches: 3, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3);
        var service = new ItemDetailFetchService(store, client, [parser], options, NullLogger<ItemDetailFetchService>.Instance);

        await service.FetchDetails(JobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.PeakInFlight, Is.LessThanOrEqualTo(3));
            Assert.That(client.CompletedCalls, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task Should_return_no_issue_when_fetch_listing_detail_succeeds()
    {
        var store = Substitute.For<IItemDetailStore>();
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>()).Returns("<html/>");
        var page = BuildPage();
        var parser = BuildParser(Marketplace.Mercari, page);
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        var issue = await service.FetchListingDetail(Target, CancellationToken.None);

        Assert.That(issue, Is.Null);
        await store.Received(1).ApplyItemDetail(Target.Id, page, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_return_an_issue_and_mark_the_listing_failed_when_fetch_listing_detail_fails()
    {
        var store = Substitute.For<IItemDetailStore>();
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Target.Url!, Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("blocked"));
        var parser = BuildParser(Marketplace.Mercari, BuildPage());
        var service = new ItemDetailFetchService(store, client, [parser], Options(), NullLogger<ItemDetailFetchService>.Instance);

        var issue = await service.FetchListingDetail(Target, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.ListingId, Is.EqualTo(Target.ListingId));
        });
        await store.Received(1).MarkDetailFetchFailed(Target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_apply_a_backfilled_detail_through_the_same_apply_item_detail_path()
    {
        var store = Substitute.For<IItemDetailStore>();
        var detail = BuildPage();
        store.GetListingEntityIds(JobId, Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("listing-1")), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>(StringComparer.Ordinal) { ["listing-1"] = 1 });
        var service = new ItemDetailFetchService(store, Substitute.For<IScrapeClient>(), [], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.ApplyBackfilledDetails(
            JobId, new Dictionary<string, ItemPageListing>(StringComparer.Ordinal) { ["listing-1"] = detail }, CancellationToken.None);

        await store.Received(1).ApplyItemDetail(1, detail, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_do_nothing_when_there_are_no_backfilled_details_to_apply()
    {
        var store = Substitute.For<IItemDetailStore>();
        var service = new ItemDetailFetchService(store, Substitute.For<IScrapeClient>(), [], Options(), NullLogger<ItemDetailFetchService>.Instance);

        await service.ApplyBackfilledDetails(
            JobId, new Dictionary<string, ItemPageListing>(StringComparer.Ordinal), CancellationToken.None);

        await store.DidNotReceive().GetListingEntityIds(
            Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().ApplyItemDetail(Arg.Any<int>(), Arg.Any<ItemPageListing>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_apply_the_remaining_backfilled_details_when_one_listing_fails_to_apply()
    {
        var store = Substitute.For<IItemDetailStore>();
        var failingDetail = BuildPage();
        var okDetail = BuildPage();
        store.GetListingEntityIds(JobId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>(StringComparer.Ordinal) { ["listing-1"] = 1, ["listing-2"] = 2 });
        store.ApplyItemDetail(1, failingDetail, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("blocked"));
        var service = new ItemDetailFetchService(store, Substitute.For<IScrapeClient>(), [], Options(), NullLogger<ItemDetailFetchService>.Instance);

        Assert.DoesNotThrowAsync(() => service.ApplyBackfilledDetails(
            JobId,
            new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
            {
                ["listing-1"] = failingDetail,
                ["listing-2"] = okDetail,
            },
            CancellationToken.None));

        await store.Received(1).ApplyItemDetail(2, okDetail, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_let_cancellation_propagate_out_of_apply_backfilled_details()
    {
        var store = Substitute.For<IItemDetailStore>();
        var detail = BuildPage();
        store.GetListingEntityIds(JobId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>(StringComparer.Ordinal) { ["listing-1"] = 1 });
        store.ApplyItemDetail(1, detail, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException());
        var service = new ItemDetailFetchService(store, Substitute.For<IScrapeClient>(), [], Options(), NullLogger<ItemDetailFetchService>.Instance);

        var thrown = Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyBackfilledDetails(
            JobId, new Dictionary<string, ItemPageListing>(StringComparer.Ordinal) { ["listing-1"] = detail }, CancellationToken.None));

        Assert.That(thrown, Is.Not.Null.And.TypeOf<OperationCanceledException>());
    }

    private static ItemPageListing BuildPage() =>
        new(
            ListingId: null,
            Title: "Item",
            Price: 10m,
            Currency: "USD",
            Condition: "Good",
            BuyingFormat: null,
            Status: "Active",
            SoldPrice: null,
            SoldDate: null,
            Seller: "Seller",
            PrimaryImageUrl: "https://img/1.jpg");

    private static IItemPageParser BuildParser(Marketplace marketplace, ItemPageListing? page)
    {
        var parser = Substitute.For<IItemPageParser>();
        parser.Marketplace.Returns(marketplace);
        parser.Parse(Arg.Any<string>()).Returns(page);
        return parser;
    }

    private static DetailFetchOptions Options() =>
        new(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3);
}
