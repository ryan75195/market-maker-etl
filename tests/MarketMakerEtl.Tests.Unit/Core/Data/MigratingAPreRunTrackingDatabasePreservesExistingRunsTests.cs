using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigratingAPreRunTrackingDatabasePreservesExistingRunsTests
{
    private const string PreChangeMigration = "20260924003128_AddListingDetailFetchAttempts";
    private const int LegacyJobId = 1;
    private const string LegacySearchTerm = "legacy pre-tracking job";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-preserve-runs-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_a_run_recorded_before_trigger_and_counter_tracking_existed()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreChangeDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var legacyRun = await db.ScrapeRuns.SingleAsync(r => r.JobId == LegacyJobId);
        var pending = await db.Database.GetPendingMigrationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(legacyRun.SearchTerm, Is.EqualTo(LegacySearchTerm));
            Assert.That(legacyRun.Status, Is.EqualTo(nameof(ScrapeRunStatus.Completed)));
            Assert.That(legacyRun.TriggerType, Is.EqualTo(nameof(TriggerType.Manual)));
            Assert.That(legacyRun.ListingsAddedActive, Is.EqualTo(0));
            Assert.That(legacyRun.TotalReportedBySearch, Is.Null);
            Assert.That(pending, Is.Empty);
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
    }
}
