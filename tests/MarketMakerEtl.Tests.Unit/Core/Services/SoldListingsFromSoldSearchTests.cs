using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldListingsFromSoldSearchTests
{
    private const string SearchTerm = "ps5";

    private const string ActiveResultsPage = "<html><body></body></html>";

    private const string SoldResultsPage = """
        <ul>
          <li class="s-item">
            <a class="s-item__link" href="https://www.ebay.co.uk/itm/111111111111?hash=item1">Sony PlayStation 5 Console</a>
            <div class="s-item__title"><span role="heading">Sony PlayStation 5 Console</span></div>
            <span class="s-item__price">£249.99</span>
            <span class="s-item__title--tagblock">Sold 12 Nov 2025</span>
          </li>
        </ul>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-sold-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_a_listing_marked_sold_after_a_sold_search_run()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var legacyActive = new ListingSummary(
            "999999999999",
            "Legacy active console",
            199m,
            "£",
            "https://www.ebay.co.uk/itm/999999999999",
            false,
            null,
            null,
            null);
        await store.UpsertListings(jobId, [legacyActive], CancellationToken.None);

        await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);
        var runs = CreateRunService(new SoldSearchScrapeClient(SoldResultsPage, ActiveResultsPage), store);

        await runs.Run(work!, CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var soldListingIds = listings.Where(l => l.IsSold).Select(l => l.ListingId).ToList();
        var soldPrice = listings.SingleOrDefault(l => l.ListingId == "111111111111")?.Price;

        Assert.Multiple(() =>
        {
            Assert.That(soldListingIds, Does.Contain("111111111111"));
            Assert.That(soldPrice, Is.EqualTo(249.99m));
            Assert.That(listings.Any(l => l.ListingId == "999999999999" && !l.IsSold), Is.True);
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
                new ScrapeOptions(MaxPages: 1, CollectSold: true)),
            store);

    private sealed class SoldSearchScrapeClient(string soldPage, string activePage) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            Task.FromResult(url.Contains("LH_Sold=1", StringComparison.Ordinal) ? soldPage : activePage);
    }
}
