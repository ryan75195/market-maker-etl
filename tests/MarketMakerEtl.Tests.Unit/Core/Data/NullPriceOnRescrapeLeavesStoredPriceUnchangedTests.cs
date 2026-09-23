using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class NullPriceOnRescrapeLeavesStoredPriceUnchangedTests
{
    private const string ListingId = "null-price-rescrape-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-null-price-{Guid.NewGuid():N}.db");
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
    public async Task Should_leave_the_stored_price_unchanged_and_record_no_history_row_when_a_rescrape_reports_a_null_price()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var original = new ListingSummary(
            ListingId,
            "PS5 Digital",
            100m,
            "USD",
            "https://www.mercari.com/us/item/null-price-rescrape-listing-001/",
            false,
            "Good",
            "https://static.mercdn.net/console.jpg",
            null);
        await store.UpsertListings(jobId, [original], CancellationToken.None);

        await store.UpsertListings(jobId, [original with { Price = null }], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        await using var db = await factory.CreateDbContextAsync();
        var storedListing = await db.Listings.SingleAsync(l => l.ListingId == ListingId);
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == storedListing.Id)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.Price, Is.EqualTo(100m));
            Assert.That(historyRows, Has.Count.EqualTo(1));
            Assert.That(historyRows.Single().Source, Is.EqualTo("InitialScrape"));
        });
    }
}
