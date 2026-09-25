using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class RunPipelineRecordsDetailFetchIssueTests
{
    private const string SearchTerm = "ps5";
    private const string ItemUrl = "https://www.ebay.co.uk/itm/123456789012";

    private const string KnownSearchPage = """
        <ul>
          <li class="s-card" data-viewport="true">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?campid=1">Sony PlayStation 5 Console</a>
            <div class="s-card__title">Sony PlayStation 5 Console</div>
            <div class="s-card__price">$250.00</div>
          </li>
        </ul>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-detail-issue-{Guid.NewGuid():N}.db");
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

    [Test]
    public async Task Should_end_the_run_completed_with_errors_and_name_the_listing_whose_item_page_failed()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var client = new BlockedItemPageScrapeClient(KnownSearchPage, ItemUrl);
        var detailFetch = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [new EbayItemPageParserService()], new DetailFetchOptions(4, 50, 1));
        var runs = new ScrapeRunService(
            new SearchPageService(
                client,
                new MarketplaceAdapters([new EbaySearchUrlService()], [new EbaySearchParser()], []),
                new ScrapeOptions(MaxPages: 1, CollectSold: false),
                TimeProvider.System,
                NullLogger<SearchPageService>.Instance),
            store,
            detailFetch,
            new ScrapeRunReportStore(factory));

        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, TriggerType.Manual, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = (await store.GetRun(runId, CancellationToken.None))!;

        Assert.Multiple(() =>
        {
            Assert.That(recordedRun.Status, Is.EqualTo(ScrapeRunStatus.CompletedWithErrors));
            Assert.That(recordedRun.Issues, Has.Count.EqualTo(1));
            Assert.That(recordedRun.Issues[0].ListingId, Is.EqualTo("123456789012"));
            Assert.That(recordedRun.Issues[0].Phase, Is.EqualTo("Detail"));
            Assert.That(recordedRun.ListingsFailed, Is.EqualTo(1));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private sealed class BlockedItemPageScrapeClient(string searchPage, string blockedUrl) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            url.StartsWith(blockedUrl, StringComparison.Ordinal)
                ? throw new InvalidOperationException("item page blocked")
                : Task.FromResult(searchPage);
    }
}
