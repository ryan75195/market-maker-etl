using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SearchObservingASaleTransitionRecordsAStatusUpdateTests
{
    private const string ListingId = "search-sale-transition-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-search-sale-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_a_status_update_row_and_mark_the_listing_sold_when_a_search_observes_the_sale_transition()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var active = BuildListing(isSold: false);
        await store.UpsertListings(jobId, [active], CancellationToken.None);

        await store.UpsertListings(jobId, [BuildListing(isSold: true)], CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var storedListing = await db.Listings.SingleAsync(l => l.ListingId == ListingId);
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == storedListing.Id)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(storedListing.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(storedListing.SoldPrice, Is.EqualTo(85m));
            Assert.That(historyRows.Count(r => r.Source == "StatusUpdate"), Is.EqualTo(1));
            var statusUpdateRow = historyRows.Single(r => r.Source == "StatusUpdate");
            Assert.That(statusUpdateRow.Status, Is.EqualTo("Sold"));
            Assert.That(statusUpdateRow.Price, Is.EqualTo(85m));
        });
    }

    [Test]
    public async Task Should_not_duplicate_the_status_update_row_when_an_already_sold_listing_is_rescraped_as_sold_again()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        await store.UpsertListings(jobId, [BuildListing(isSold: false)], CancellationToken.None);
        await store.UpsertListings(jobId, [BuildListing(isSold: true)], CancellationToken.None);

        await store.UpsertListings(jobId, [BuildListing(isSold: true)], CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var storedListing = await db.Listings.SingleAsync(l => l.ListingId == ListingId);
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == storedListing.Id)
            .ToListAsync();

        Assert.That(historyRows.Count(r => r.Source == "StatusUpdate"), Is.EqualTo(1));
    }

    private static ListingSummary BuildListing(bool isSold) =>
        new(
            ListingId,
            "PS5 Digital",
            85m,
            "USD",
            "https://www.mercari.com/us/item/search-sale-transition-listing-001/",
            isSold,
            "Good",
            "https://static.mercdn.net/console.jpg",
            null);
}
