using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingMigrationPreservesExistingRowsAndValuesTests
{
    private const string LatestMigrationBeforeStatusFields = "20260911130612_AddListingFields";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-preserve-listings-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_existing_listing_rows_and_values_when_applying_the_new_migration()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(LatestMigrationBeforeStatusFields);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Listings (ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, Condition, PrimaryImageUrl, BuyingFormat, CreatedUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})",
                "legacy-listing-001",
                0,
                "Legacy PS5",
                199.99m,
                "GBP",
                "https://example.test/itm/legacy",
                true,
                "Used",
                "https://example.test/legacy.png",
                "Auction",
                DateTime.UtcNow);
        }

        await factory.ApplyMigrations(CancellationToken.None);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = await db.Listings.SingleAsync(l => l.ListingId == "legacy-listing-001");

            Assert.Multiple(() =>
            {
                Assert.That(listing.Title, Is.EqualTo("Legacy PS5"));
                Assert.That(listing.Price, Is.EqualTo(199.99m));
                Assert.That(listing.Currency, Is.EqualTo("GBP"));
                Assert.That(listing.Url, Is.EqualTo("https://example.test/itm/legacy"));
                Assert.That(listing.IsSold, Is.True);
                Assert.That(listing.Condition, Is.EqualTo("Used"));
                Assert.That(listing.PrimaryImageUrl, Is.EqualTo("https://example.test/legacy.png"));
                Assert.That(listing.BuyingFormat, Is.EqualTo("Auction"));
                Assert.That(listing.ItemStatus, Is.Null);
                Assert.That(listing.SoldPrice, Is.Null);
                Assert.That(listing.SoldDate, Is.Null);
                Assert.That(listing.Seller, Is.Null);
            });
        }
    }
}
