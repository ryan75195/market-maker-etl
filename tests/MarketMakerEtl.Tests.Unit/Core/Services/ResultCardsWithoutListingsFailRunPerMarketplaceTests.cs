using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ResultCardsWithoutListingsFailRunPerMarketplaceTests
{
    private const string SearchTerm = "ps5";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-marketplace-guard-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [TestCase(Marketplace.Ebay)]
    [TestCase(Marketplace.Mercari)]
    public async Task Should_fail_the_run_when_the_page_rendered_result_cards_but_produced_no_listings(
        Marketplace marketplace)
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None, marketplace);
        var runId = await store.EnqueueRun(jobId, SearchTerm, TriggerType.Manual, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);
        var runs = CreateRunService(marketplace, containsListingMarkup: true, store);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = await store.GetRun(runId, CancellationToken.None);
        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recordedRun?.Status, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(listings, Is.Empty);
        });
    }

    [TestCase(Marketplace.Ebay)]
    [TestCase(Marketplace.Mercari)]
    public async Task Should_complete_the_run_when_the_search_is_genuinely_empty(Marketplace marketplace)
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None, marketplace);
        var runId = await store.EnqueueRun(jobId, SearchTerm, TriggerType.Manual, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);
        var runs = CreateRunService(marketplace, containsListingMarkup: false, store);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = await store.GetRun(runId, CancellationToken.None);
        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recordedRun?.Status, Is.EqualTo(ScrapeRunStatus.Completed));
            Assert.That(listings, Is.Empty);
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private ScrapeRunService CreateRunService(
        Marketplace marketplace,
        bool containsListingMarkup,
        ScrapeStore store)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");

        var urls = Substitute.For<IEbaySearchUrlService>();
        urls.Marketplace.Returns(marketplace);
        urls.SupportsPagination.Returns(true);
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>()).Returns("https://search");

        var parser = Substitute.For<ISearchPageParser>();
        parser.Marketplace.Returns(marketplace);
        parser.ContainsListingMarkup(Arg.Any<string>()).Returns(containsListingMarkup);
        parser.Parse(Arg.Any<string>()).Returns(new SearchPageResult([], null));

        var search = new SearchPageService(
            client,
            new MarketplaceAdapters([urls], [parser], []),
            new ScrapeOptions(MaxPages: 1, CollectSold: false),
            new DetailFetchOptions(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3),
            NullLogger<SearchPageService>.Instance);

        var detailFetch = Substitute.For<IItemDetailFetchService>();
        detailFetch.FetchDetails(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScrapeRunIssueDetails>());

        return new ScrapeRunService(
            search,
            store,
            detailFetch,
            new ScrapeRunReportStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>()));
    }
}
