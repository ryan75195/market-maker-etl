using System.Data.Common;
using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigratingAPreChangeDatabasePreservesListingsAndHistoryTests
{
    private const string PreChangeMigration = "20260915000000_AddListingBrand";
    private const int LegacyListingId = 1;
    private const string LegacyListingIdentifier = "legacy-history-listing";
    private const string LegacyStatus = "Active";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-preserve-history-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_every_listing_and_history_row_when_migrating_a_pre_change_database()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreChangeDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var listingColumns = await ReadColumnNamesAsync(connection, "Listings");
        var historyColumns = await ReadColumnNamesAsync(connection, "ListingStatusChanges");
        var legacyListing = await db.Listings.SingleAsync(l => l.ListingId == LegacyListingIdentifier);
        var legacyHistory = await db.ListingStatusChanges.SingleAsync(c => c.ListingEntityId == LegacyListingId);

        Assert.Multiple(() =>
        {
            Assert.That(listingColumns, Does.Contain("Category"));
            Assert.That(historyColumns, Does.Contain("Source"));
            Assert.That(legacyListing.Title, Is.EqualTo("Legacy PS5"));
            Assert.That(legacyListing.Price, Is.EqualTo(199.99m));
            Assert.That(legacyHistory.Status, Is.EqualTo(LegacyStatus));
            Assert.That(legacyHistory.Source, Is.EqualTo("StatusUpdate"));
        });
    }

    private static async Task SeedPreChangeDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreChangeMigration);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Listings (Id, ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, CreatedUtc) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            LegacyListingId,
            LegacyListingIdentifier,
            0,
            "Legacy PS5",
            199.99m,
            "GBP",
            "https://example.test/itm/legacy",
            false,
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ListingStatusChanges (ListingEntityId, Status, ChangedUtc) VALUES ({0}, {1}, {2})",
            LegacyListingId,
            LegacyStatus,
            DateTime.UtcNow);
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
