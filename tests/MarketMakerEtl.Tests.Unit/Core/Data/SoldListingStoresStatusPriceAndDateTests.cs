using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SoldListingStoresStatusPriceAndDateTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-sold-listing-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
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
    public async Task Should_store_and_read_back_a_sold_listing_with_its_status_price_and_date()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await factory.ApplyMigrations(CancellationToken.None);

        var soldDate = new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);
        var listingId = 0;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = new ListingEntity
            {
                ListingId = "sold-listing-777",
                ScrapeJobId = 0,
                Title = "Sold PS5",
                IsSold = true,
                ItemStatus = "Sold",
                SoldPrice = 275.50m,
                SoldDate = soldDate,
                CreatedUtc = DateTime.UtcNow
            };
            db.Listings.Add(listing);
            await db.SaveChangesAsync();
            listingId = listing.Id;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.Listings.SingleAsync(l => l.Id == listingId);

            Assert.Multiple(() =>
            {
                Assert.That(stored.IsSold, Is.True);
                Assert.That(stored.ItemStatus, Is.EqualTo("Sold"));
                Assert.That(stored.SoldPrice, Is.EqualTo(275.50m));
                Assert.That(stored.SoldDate, Is.EqualTo(soldDate));
            });
        }
    }
}
