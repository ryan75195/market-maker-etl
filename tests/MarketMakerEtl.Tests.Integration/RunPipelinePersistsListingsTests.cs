using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class RunPipelinePersistsListingsTests
{
    private const string SearchTerm = "ps5";

    private const string KnownSearchPage = """
        <ul>
          <li class="s-card" data-viewport="true">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?campid=1">Sony PlayStation 5 Console</a>
            <div class="s-card__title">Sony PlayStation 5 Console</div>
            <div class="s-card__price">$250.00</div>
          </li>
          <li class="s-card" data-viewport="true">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/987654321098?campid=2">Sony PS5 DualSense Controller</a>
            <div class="s-card__title">Sony PS5 DualSense Controller</div>
            <div class="s-card__price">$39.99</div>
            <span class="POSITIVE">Sold</span>
          </li>
        </ul>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_the_expected_listings_from_a_known_search_page()
    {
        var store = CreateStore();
        var runs = CreateRunService(new StubScrapeClient(KnownSearchPage), store);

        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await runs.Run(work!, CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var recordedRun = (await store.GetRun(runId, CancellationToken.None))!;

        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(2));

            var active = listings.Single(l => l.ListingId == "123456789012");
            Assert.That(active.Title, Is.EqualTo("Sony PlayStation 5 Console"));
            Assert.That(active.Price, Is.EqualTo(250.00m));
            Assert.That(active.Currency, Is.EqualTo("$"));
            Assert.That(active.Url, Is.EqualTo("https://www.ebay.co.uk/itm/123456789012"));
            Assert.That(active.IsSold, Is.False);

            var sold = listings.Single(l => l.ListingId == "987654321098");
            Assert.That(sold.Title, Is.EqualTo("Sony PS5 DualSense Controller"));
            Assert.That(sold.Price, Is.EqualTo(39.99m));
            Assert.That(sold.IsSold, Is.True);

            Assert.That(recordedRun.Status, Is.EqualTo(ScrapeRunStatus.Completed));
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
            store,
            Substitute.For<IItemDetailFetchService>());

    private sealed class StubScrapeClient(string html) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) => Task.FromResult(html);
    }
}
