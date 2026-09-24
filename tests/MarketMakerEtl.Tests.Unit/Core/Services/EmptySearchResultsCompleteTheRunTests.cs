using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EmptySearchResultsCompleteTheRunTests
{
    private const string SearchTerm = "ps5";

    private const string EmptyResultsPage = """
        <html>
          <body>
            <div class="srp-save-null-search">
              <h1>No exact matches found</h1>
            </div>
          </body>
        </html>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-empty-{Guid.NewGuid():N}.db");
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
    public async Task Should_complete_the_run_with_zero_listings_for_a_genuinely_empty_result_page()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);
        var runs = CreateRunService(new SinglePageScrapeClient(EmptyResultsPage), store);

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

    private static ScrapeRunService CreateRunService(IScrapeClient client, ScrapeStore store) =>
        new(
            new SearchPageService(
                client,
                [new EbaySearchUrlService()],
                [new EbaySearchParser()],
                new ScrapeOptions(MaxPages: 3, CollectSold: false),
                NullLogger<SearchPageService>.Instance),
            store,
            new NoOpItemDetailFetchService());

    private sealed class SinglePageScrapeClient(string html) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) => Task.FromResult(html);
    }
}
