using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ScrapeRunServiceTests
{
    private static readonly ScrapeRunWork Work = new(RunId: 1, JobId: 2, SearchTerm: "ps5");

    [Test]
    public async Task Should_persist_listings_fetch_item_detail_and_complete_the_run()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", false, null, null, null)],
                TotalReportedBySearch: 1,
                Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(1, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var reports = Substitute.For<IScrapeRunReportStore>();
        var service = new ScrapeRunService(search, store, detailFetch, reports);

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).UpsertListings(2, Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>());
        await detailFetch.Received(1).FetchDetails(2, Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRun(
            1,
            Arg.Is<RunCompletionCounts>(counts =>
                counts.ListingsAddedActive == 1
                && counts.ListingsFailed == 0
                && counts.TotalListingsFound == 1
                && counts.TotalReportedBySearch == 1),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().FailRun(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_fail_the_run_when_collection_throws()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns<SearchCollectionResult>(_ => throw new InvalidOperationException("scraper down"));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).FailRun(1, "scraper down", Arg.Any<CancellationToken>());
        await store.DidNotReceive().CompleteRun(Arg.Any<int>(), Arg.Any<RunCompletionCounts>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_fail_the_run_when_item_detail_fetching_throws()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", false, null, null, null)],
                TotalReportedBySearch: 1,
                Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(1, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(2, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ScrapeRunIssueDetails>>(_ => throw new InvalidOperationException("detail store unavailable"));
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).FailRun(1, "detail store unavailable", Arg.Any<CancellationToken>());
        await store.DidNotReceive().CompleteRun(Arg.Any<int>(), Arg.Any<RunCompletionCounts>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_fail_the_run_with_the_innermost_exception_message_when_the_wrapper_hides_the_cause()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", false, null, null, null)],
                TotalReportedBySearch: 1,
                Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(1, 0, 0, 0));
        var innermost = new Microsoft.Data.Sqlite.SqliteException("SQLite Error 5: 'database is locked'.", 5);
        var outer = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "An error occurred while saving the entity changes.", innermost);
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(2, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ScrapeRunIssueDetails>>(_ => throw outer);
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).FailRun(
            1,
            Arg.Is<string>(message =>
                message.Contains("SQLite Error 5: 'database is locked'.")
                && message.Contains(nameof(Microsoft.Data.Sqlite.SqliteException))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_record_a_detail_fetch_issue_and_count_it_as_a_failed_listing()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", false, null, null, null)],
                TotalReportedBySearch: 1,
                Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(1, 0, 0, 0));
        var issue = new ScrapeRunIssueDetails("111111111111", "ItemDetailFetchFailed", "blocked", "Detail", null);
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails> { issue });
        var reports = Substitute.For<IScrapeRunReportStore>();
        var service = new ScrapeRunService(search, store, detailFetch, reports);

        await service.Run(Work, CancellationToken.None);

        await reports.Received(1).RecordIssue(1, issue, Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRun(
            1,
            Arg.Is<RunCompletionCounts>(counts => counts.ListingsFailed == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_record_backfill_item_page_fetches_on_the_completed_run()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [],
                TotalReportedBySearch: 0,
                Issues: [],
                BackfillItemPageFetches: 7));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(0, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).CompleteRun(
            1,
            Arg.Is<RunCompletionCounts>(counts => counts.BackfillItemPageFetches == 7),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_apply_backfilled_details_before_fetching_the_remaining_detail_targets()
    {
        var detail = new ItemPageListing(
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
            PrimaryImageUrl: null);
        var backfilledDetails = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
        {
            ["111111111111"] = detail,
        };
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult(
                [new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", true, null, null, null)],
                TotalReportedBySearch: 1,
                Issues: [],
                BackfilledDetails: backfilledDetails));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(0, 1, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        Received.InOrder(() =>
        {
            detailFetch.ApplyBackfilledDetails(2, backfilledDetails, Arg.Any<CancellationToken>());
            detailFetch.FetchDetails(2, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Should_not_call_apply_backfilled_details_when_the_search_found_nothing_to_backfill()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([], TotalReportedBySearch: null, Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<ListingSummary>());
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(0, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await detailFetch.DidNotReceive().ApplyBackfilledDetails(
            Arg.Any<int>(), Arg.Any<IReadOnlyDictionary<string, ItemPageListing>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_pass_the_jobs_existing_sold_listing_ids_as_known_sold_listings()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([], TotalReportedBySearch: null, Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(2, Arg.Any<CancellationToken>()).Returns(
            [new ListingSummary("999999999999", "Existing", 1m, "GBP", "https://x/itm/9", true, null, null, null)]);
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(0, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await search.Received(1).Collect(
            "ps5",
            Marketplace.Ebay,
            Arg.Is<IReadOnlySet<string>>(known => known.Contains("999999999999")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_not_treat_a_previously_active_listing_as_known_when_it_has_not_been_recorded_as_sold()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Marketplace.Ebay, Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SearchCollectionResult([], TotalReportedBySearch: null, Issues: []));
        var store = Substitute.For<IScrapeStore>();
        store.GetListings(2, Arg.Any<CancellationToken>()).Returns(
            [new ListingSummary("777777777777", "Still active", 1m, "GBP", "https://x/itm/7", false, null, null, null)]);
        store.UpsertListings(Arg.Any<int>(), Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>())
            .Returns(new ListingUpsertSummary(0, 0, 0, 0));
        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());
        var service = new ScrapeRunService(search, store, detailFetch, Substitute.For<IScrapeRunReportStore>());

        await service.Run(Work, CancellationToken.None);

        await search.Received(1).Collect(
            "ps5",
            Marketplace.Ebay,
            Arg.Is<IReadOnlySet<string>>(known => !known.Contains("777777777777")),
            Arg.Any<CancellationToken>());
    }
}
