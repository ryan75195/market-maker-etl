using System.Data.Common;
using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class JobSchedulingMigrationPreservesExistingDataTests
{
    private const string PreSchedulingMigration = "20260915000000_AddListingBrand";
    private const string LegacyListingId = "legacy-scheduling-listing";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-job-scheduling-migration-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_existing_jobs_runs_and_listings_and_default_interval_hours_to_24()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreSchedulingDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var job = await db.ScrapeJobs.SingleAsync(j => j.SearchTerm == "legacy scheduling job");
        var run = await db.ScrapeRuns.SingleAsync(r => r.JobId == job.Id);
        var listing = await db.Listings.SingleAsync(l => l.ListingId == LegacyListingId);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var scrapeJobColumns = await ReadColumnNamesAsync(connection, "ScrapeJobs");

        Assert.Multiple(() =>
        {
            Assert.That(job.IntervalHours, Is.EqualTo(24));
            Assert.That(run.SearchTerm, Is.EqualTo("legacy scheduling job"));
            Assert.That(listing.Title, Is.EqualTo("Legacy Console"));
            Assert.That(listing.Price, Is.EqualTo(149.99m));
            Assert.That(scrapeJobColumns, Does.Contain("FilterInstructions"));
            Assert.That(scrapeJobColumns, Does.Contain("LastQueuedUtc"));
            Assert.That(scrapeJobColumns, Does.Contain("LastRunUtc"));
        });
    }

    private static async Task SeedPreSchedulingDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreSchedulingMigration);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeJobs (SearchTerm, Marketplace, IsEnabled, CreatedUtc) VALUES ({0}, {1}, {2}, {3})",
            "legacy scheduling job",
            1,
            true,
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ScrapeRuns (JobId, Marketplace, SearchTerm, Status, StartedUtc) VALUES ({0}, {1}, {2}, {3}, {4})",
            1,
            1,
            "legacy scheduling job",
            "Completed",
            DateTime.UtcNow);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Listings (ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, CreatedUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
            LegacyListingId,
            1,
            "Legacy Console",
            149.99m,
            "USD",
            "https://example.test/itm/legacy-scheduling",
            false,
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
