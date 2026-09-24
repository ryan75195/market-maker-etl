using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class RefreshObservingASaleRecordsAStatusUpdateWithPriceAndSoldDateTests
{
    private static readonly DateTime SoldDate = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-sale-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_a_status_update_row_with_the_sold_price_and_sold_date_when_a_refresh_observes_a_sale()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var store = new ScrapeStore(factory, new ScrapeRunStateService());

        await store.RecordStatusChange(
            listingId,
            new ListingStatusObservation("Sold", null, 180.50m, SoldDate, "Retro Games Shop", true),
            CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var historyRows = await db.ListingStatusChanges
            .Where(c => c.ListingEntityId == listingId)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(historyRows, Has.Count.EqualTo(1));
            Assert.That(historyRows[0].Source, Is.EqualTo("StatusUpdate"));
            Assert.That(historyRows[0].Price, Is.EqualTo(180.50m));
            Assert.That(historyRows[0].SoldDateUtc, Is.EqualTo(SoldDate));
        });
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "refresh-sale-listing-001",
            Url = "https://www.mercari.com/us/item/refresh-sale-listing-001/",
            ItemStatus = "Active",
            Price = 220m,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
