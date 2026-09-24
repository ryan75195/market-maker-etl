using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldListingFromSearchGetsSoldDateAndHistoryFromDetailFetchTests
{
    private const string ListingUrl = "https://www.mercari.com/us/item/m44688360101/";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-sold-search-detail-{Guid.NewGuid():N}.db");
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
    public async Task Should_end_the_run_with_sold_date_sold_price_and_a_history_row_for_a_listing_a_sold_search_already_marked_sold()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedJob(factory);
        var listingEntityId = await SeedAlreadySoldListingWithNoSoldDate(factory, jobId);
        var html = ReadFixture("item-sold-m44688360101.html");
        var client = new StubScrapeClient(new Dictionary<string, string> { [ListingUrl] = html });
        var service = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [new MercariItemPageParser()], new DetailFetchOptions(4, 50));

        await service.FetchDetails(jobId, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingEntityId);
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == listingEntityId)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(listing.SoldPrice, Is.EqualTo(140.25m));
            Assert.That(listing.SoldDate, Is.EqualTo(new DateTime(2026, 9, 23, 22, 21, 59, DateTimeKind.Utc)));
            Assert.That(historyRows.Count(r => r.Source == "StatusUpdate"), Is.EqualTo(1));
            var row = historyRows.Single(r => r.Source == "StatusUpdate");
            Assert.That(row.Price, Is.EqualTo(140.25m));
            Assert.That(row.SoldDateUtc, Is.EqualTo(new DateTime(2026, 9, 23, 22, 21, 59, DateTimeKind.Utc)));
        });
    }

    private static async Task<int> SeedJob(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<int> SeedAlreadySoldListingWithNoSoldDate(IDbContextFactory<EtlDbContext> factory, int jobId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "m44688360101",
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = ListingUrl,
            ItemStatus = "Sold",
            IsSold = true,
            Price = 140.25m,
            SoldPrice = null,
            SoldDate = null,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
