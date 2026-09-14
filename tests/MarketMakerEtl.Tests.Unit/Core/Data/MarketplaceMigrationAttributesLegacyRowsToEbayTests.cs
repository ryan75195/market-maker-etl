using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MarketplaceMigrationAttributesLegacyRowsToEbayTests
{
    private const string PreMarketplaceMigration = "20260911221032_AddListingStatusAndSaleFields";
    private const string LegacyListingId = "legacy-marketplace-listing";
    private const string LegacySearchTerm = "legacy-marketplace-search";

    private static readonly DateTime LegacyCreatedUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime LegacyStartedUtc = new(2026, 1, 2, 3, 5, 0, DateTimeKind.Utc);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-marketplace-migration-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_legacy_rows_and_attribute_them_to_ebay_after_migrating()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreMarketplaceDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var job = await db.ScrapeJobs.SingleAsync(j => j.SearchTerm == LegacySearchTerm);
        var run = await db.ScrapeRuns.SingleAsync(r => r.SearchTerm == LegacySearchTerm);
        var listing = await db.Listings.SingleAsync(l => l.ListingId == LegacyListingId);

        Assert.Multiple(() =>
        {
            Assert.That(job.Marketplace, Is.EqualTo(Marketplace.Ebay));
            Assert.That(run.Marketplace, Is.EqualTo(Marketplace.Ebay));
            Assert.That(listing.Marketplace, Is.EqualTo(Marketplace.Ebay));
            Assert.That(listing.Title, Is.EqualTo("Legacy PS5"));
            Assert.That(listing.Price, Is.EqualTo(199.99m));
            Assert.That(listing.IsSold, Is.True);
        });
    }

    private static async Task SeedPreMarketplaceDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreMarketplaceMigration);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeJobs (SearchTerm, IsEnabled, CreatedUtc) VALUES ({0}, {1}, {2})",
            LegacySearchTerm,
            true,
            LegacyCreatedUtc);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeRuns (JobId, SearchTerm, Status, StartedUtc) VALUES ({0}, {1}, {2}, {3})",
            0,
            LegacySearchTerm,
            "Queued",
            LegacyStartedUtc);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Listings (ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, CreatedUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
            LegacyListingId,
            0,
            "Legacy PS5",
            199.99m,
            "GBP",
            "https://example.test/itm/legacy",
            true,
            LegacyCreatedUtc);
    }
}
