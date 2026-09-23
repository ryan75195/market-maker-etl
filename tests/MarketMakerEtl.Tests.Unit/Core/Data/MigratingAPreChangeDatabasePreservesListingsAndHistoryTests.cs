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
    private const string PreChangeMigration = "20260923231305_AddJobSchedulingAndCategories";
    private const int LegacyJobId = 1;
    private const int LegacyCategoryId = 1;
    private const int LegacyListingId = 1;
    private const string LegacyListingIdentifier = "legacy-history-listing";
    private const string LegacyStatus = "Active";
    private const string LegacySearchTerm = "legacy pre-detail job";
    private const string LegacyCategoryName = "legacy pre-detail category";

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
        var legacyJob = await db.ScrapeJobs.SingleAsync(j => j.SearchTerm == LegacySearchTerm);
        var legacyRun = await db.ScrapeRuns.SingleAsync(r => r.JobId == legacyJob.Id);
        var legacyCategory = await db.Categories.SingleAsync(c => c.Name == LegacyCategoryName);
        var legacyJobCategory = await db.JobCategories.SingleAsync(jc => jc.ScrapeJobId == legacyJob.Id);

        Assert.Multiple(() =>
        {
            Assert.That(listingColumns, Does.Contain("Category"));
            Assert.That(historyColumns, Does.Contain("Source"));
            Assert.That(legacyListing.Title, Is.EqualTo("Legacy PS5"));
            Assert.That(legacyListing.Price, Is.EqualTo(199.99m));
            Assert.That(legacyHistory.Status, Is.EqualTo(LegacyStatus));
            Assert.That(legacyHistory.Source, Is.EqualTo("StatusUpdate"));
            Assert.That(legacyJob.SearchTerm, Is.EqualTo(LegacySearchTerm));
            Assert.That(legacyJob.IntervalHours, Is.EqualTo(24));
            Assert.That(legacyRun.SearchTerm, Is.EqualTo(LegacySearchTerm));
            Assert.That(legacyCategory.Name, Is.EqualTo(LegacyCategoryName));
            Assert.That(legacyJobCategory.CategoryId, Is.EqualTo(legacyCategory.Id));
        });
    }

    private static async Task SeedPreChangeDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreChangeMigration);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeJobs (Id, SearchTerm, Marketplace, IsEnabled, CreatedUtc) VALUES ({0}, {1}, {2}, {3}, {4})",
            LegacyJobId,
            LegacySearchTerm,
            1,
            true,
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeRuns (JobId, Marketplace, SearchTerm, Status, StartedUtc) VALUES ({0}, {1}, {2}, {3}, {4})",
            LegacyJobId,
            1,
            LegacySearchTerm,
            "Completed",
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Listings (Id, ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, CreatedUtc) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            LegacyListingId,
            LegacyListingIdentifier,
            LegacyJobId,
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

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Categories (Id, Name, IsEnabled, CreatedUtc) VALUES ({0}, {1}, {2}, {3})",
            LegacyCategoryId,
            LegacyCategoryName,
            true,
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO JobCategories (ScrapeJobId, CategoryId) VALUES ({0}, {1})",
            LegacyJobId,
            LegacyCategoryId);
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
