using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class BackfilledSoldDetailsSkipTheDetailPhaseTests
{
    private const string SearchTerm = "ps5 controller";
    private const string ListingId = "m44688360101";
    private const string ListingUrl = "https://www.mercari.com/us/item/m44688360101/";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-backfill-reuse-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_a_backfilled_sold_listing_through_item_detail_store_and_skip_it_in_the_detail_phase()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var html = ReadFixture("item-sold-m44688360101.html");
        var client = new StubScrapeClient(new Dictionary<string, string> { [ListingUrl] = html });
        var itemParser = new MercariItemPageParser();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var itemDetailStore = new ItemDetailStore(factory);
        var detailFetch = new ItemDetailFetchService(itemDetailStore, client, [itemParser], new DetailFetchOptions(4, 50, 3));
        var search = new BackfillSimulatingSearchPageService(client, itemParser);
        var runs = new ScrapeRunService(search, store, detailFetch, new ScrapeRunReportStore(factory));

        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None, Marketplace.Mercari);
        var runId = await store.EnqueueRun(jobId, SearchTerm, TriggerType.Manual, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await runs.Run(work!, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.ListingId == ListingId);
        var rawData = await db.ListingRawData.SingleOrDefaultAsync(r => r.ListingEntityId == listing.Id);
        var seller = await db.Sellers.SingleOrDefaultAsync(s => s.SellerId == 993368787);

        Assert.Multiple(() =>
        {
            Assert.That(listing.SoldPrice, Is.EqualTo(140.25m));
            Assert.That(listing.SoldDate, Is.EqualTo(new DateTime(2026, 9, 23, 22, 21, 59, DateTimeKind.Utc)));
            Assert.That(listing.Description, Does.Contain("Ps5 Controller Anniversary Edition"));
            Assert.That(listing.DetailFetchedUtc, Is.Not.Null);
            Assert.That(rawData, Is.Not.Null);
            Assert.That(rawData!.ItemDetailJsonGzip, Is.Not.Null);
            Assert.That(seller, Is.Not.Null);
            Assert.That(seller!.Name, Is.EqualTo("Tobey Maguire"));
            Assert.That(client.RequestedUrls.Count(url => url == ListingUrl), Is.EqualTo(1));
        });
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));

    private sealed class BackfillSimulatingSearchPageService : ISearchPageService
    {
        private readonly IScrapeClient _client;
        private readonly IItemPageParser _itemParser;

        internal BackfillSimulatingSearchPageService(IScrapeClient client, IItemPageParser itemParser)
        {
            _client = client;
            _itemParser = itemParser;
        }

        public async Task<SearchCollectionResult> Collect(
            string searchTerm, Marketplace marketplace, IReadOnlySet<string> knownSoldListingIds, CancellationToken ct)
        {
            var html = await _client.GetPageHtml(ListingUrl, ct);
            var detail = _itemParser.Parse(html)!;
            var listing = new ListingSummary(
                ListingId, "PS5 DualSense Controller", 140.25m, "USD", ListingUrl, true, null, null, null);
            var backfilledDetails = new Dictionary<string, ItemPageListing>(StringComparer.Ordinal)
            {
                [ListingId] = detail,
            };

            return new SearchCollectionResult(
                [listing], TotalReportedBySearch: 1, Issues: [], BackfillItemPageFetches: 1, BackfilledDetails: backfilledDetails);
        }
    }
}
