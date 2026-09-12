using System.Data.Common;
using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class LegacyDatabaseMigrationPreservesListingRowsAndValuesTests
{
    private const string InitialMigration = "20260911124753_InitialCreate";
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";
    private const string LegacyListingId = "legacy-listing-001";
    private const string LegacySearchTerm = "legacy-search";

    private static readonly DateTime LegacyCreatedUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime LegacyStartedUtc = new(2026, 1, 2, 3, 5, 0, DateTimeKind.Utc);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-legacy-rows-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_existing_listing_and_run_values_when_upgrading_a_legacy_database()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedLegacyDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        HashSet<string> listingColumns;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            listingColumns = await ReadColumnNamesAsync(connection, "Listings");
        }

        Assert.That(listingColumns, Does.Contain("ItemStatus"));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listings = await db.Listings.Where(l => l.ScrapeJobId == 0).ToListAsync();
            var listing = await db.Listings.SingleAsync(l => l.ListingId == LegacyListingId);
            var run = await db.ScrapeRuns.SingleAsync(r => r.SearchTerm == LegacySearchTerm);

            Assert.Multiple(() =>
            {
                Assert.That(listings, Has.Count.EqualTo(1));
                Assert.That(listing.Title, Is.EqualTo("Legacy PS5"));
                Assert.That(listing.Price, Is.EqualTo(199.99m));
                Assert.That(listing.Currency, Is.EqualTo("GBP"));
                Assert.That(listing.Url, Is.EqualTo("https://example.test/itm/legacy"));
                Assert.That(listing.IsSold, Is.True);
                Assert.That(listing.ItemStatus, Is.Null);
                Assert.That(listing.SoldPrice, Is.Null);
                Assert.That(listing.SoldDate, Is.Null);
                Assert.That(run.SearchTerm, Is.EqualTo(LegacySearchTerm));
                Assert.That(run.Status, Is.EqualTo("Queued"));
            });
        }
    }

    private static async Task SeedLegacyDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);
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
        await db.Database.ExecuteSqlRawAsync("DROP TABLE " + MigrationsHistoryTable);
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$table";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
