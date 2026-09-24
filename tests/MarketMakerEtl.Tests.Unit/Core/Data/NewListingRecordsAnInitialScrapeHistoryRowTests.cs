using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class NewListingRecordsAnInitialScrapeHistoryRowTests
{
    private const string ListingId = "initial-scrape-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-initial-scrape-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_exactly_one_initial_scrape_history_row_with_the_listings_price_for_a_new_listing()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var listing = new ListingSummary(
            ListingId,
            "PS5 Digital",
            349.99m,
            "USD",
            "https://www.mercari.com/us/item/initial-scrape-listing-001/",
            false,
            "Good",
            "https://static.mercdn.net/console.jpg",
            null);

        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var storedListing = await db.Listings.SingleAsync(l => l.ListingId == ListingId);
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == storedListing.Id)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(historyRows, Has.Count.EqualTo(1));
            Assert.That(historyRows[0].Source, Is.EqualTo("InitialScrape"));
            Assert.That(historyRows[0].Price, Is.EqualTo(349.99m));
        });
    }
}
