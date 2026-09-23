using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class RescrapingAtANewPriceRecordsAPriceUpdateTests
{
    private const string ListingId = "price-update-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-price-update-{Guid.NewGuid():N}.db");
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
    public async Task Should_update_the_stored_price_and_add_exactly_one_price_update_row_when_rescraped_at_a_different_price()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var original = BuildListing(300m);
        await store.UpsertListings(jobId, [original], CancellationToken.None);

        await store.UpsertListings(jobId, [original with { Price = 275m }], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var historyRows = await ReadHistoryRows(factory, ListingId);

        Assert.Multiple(() =>
        {
            Assert.That(listings.Single().Price, Is.EqualTo(275m));
            Assert.That(historyRows.Count(r => r.Source == "PriceUpdate"), Is.EqualTo(1));
            Assert.That(historyRows.Single(r => r.Source == "PriceUpdate").Price, Is.EqualTo(275m));
        });
    }

    [Test]
    public async Task Should_add_no_additional_history_row_when_rescraped_at_the_same_price()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var listing = BuildListing(300m);
        await store.UpsertListings(jobId, [listing], CancellationToken.None);
        var countAfterFirstScrape = (await ReadHistoryRows(factory, ListingId)).Count;

        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        var historyRows = await ReadHistoryRows(factory, ListingId);
        Assert.That(historyRows, Has.Count.EqualTo(countAfterFirstScrape));
    }

    private static ListingSummary BuildListing(decimal price) =>
        new(
            ListingId,
            "PS5 Digital",
            price,
            "USD",
            "https://www.mercari.com/us/item/price-update-listing-001/",
            false,
            "Good",
            "https://static.mercdn.net/console.jpg",
            null);

    private static async Task<List<ListingStatusChangeEntity>> ReadHistoryRows(
        IDbContextFactory<EtlDbContext> factory, string listingId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var storedListing = await db.Listings.SingleAsync(l => l.ListingId == listingId);
        return await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == storedListing.Id)
            .ToListAsync();
    }
}
