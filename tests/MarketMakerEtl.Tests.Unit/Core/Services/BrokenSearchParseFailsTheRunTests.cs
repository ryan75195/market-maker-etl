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
public class BrokenSearchParseFailsTheRunTests
{
    private const string SearchTerm = "ps5";

    private const string LinksThatDoNotParse = """
        <ul>
          <li class="s-item">
            <a class="s-item__link" href="https://www.ebay.co.uk/itm/not-a-listing-id">Sony PlayStation 5 Console</a>
            <div class="s-item__title"><span role="heading">Sony PlayStation 5 Console</span></div>
            <span class="s-item__price">£249.99</span>
          </li>
        </ul>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-broken-{Guid.NewGuid():N}.db");
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
    public async Task Should_fail_the_run_when_a_page_has_listing_links_but_parses_to_zero_results()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);
        var runs = CreateRunService(new SinglePageScrapeClient(LinksThatDoNotParse), store);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = await store.GetRun(runId, CancellationToken.None);
        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recordedRun?.Status, Is.EqualTo(ScrapeRunStatus.Failed));
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
                new ScrapeOptions(MaxPages: 1, CollectSold: false),
                NullLogger<SearchPageService>.Instance),
            store);

    private sealed class SinglePageScrapeClient(string html) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) => Task.FromResult(html);
    }
}
