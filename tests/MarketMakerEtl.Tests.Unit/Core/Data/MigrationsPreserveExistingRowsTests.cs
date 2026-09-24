using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigrationsPreserveExistingRowsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-preserve-{Guid.NewGuid():N}.db");
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
    public async Task Should_preserve_rows_created_before_migrations_were_adopted()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("legacy-search", CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, "legacy-search", TriggerType.Manual, CancellationToken.None);
        var listing = new ListingSummary("222222222222", "Legacy PS5", 250m, "GBP", "https://x/itm/2", true, null, null, null);
        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        await factory.ApplyMigrations(CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var run = await store.GetRun(runId, CancellationToken.None);

        IReadOnlyList<string> pending;
        await using (var db = await factory.CreateDbContextAsync())
        {
            pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(1));
            Assert.That(listings[0].Title, Is.EqualTo("Legacy PS5"));
            Assert.That(listings[0].Price, Is.EqualTo(250m));
            Assert.That(listings[0].IsSold, Is.True);
            Assert.That(run, Is.Not.Null);
            Assert.That(run!.SearchTerm, Is.EqualTo("legacy-search"));
            Assert.That(run.Status, Is.EqualTo(ScrapeRunStatus.Queued));
            Assert.That(pending, Is.Empty);
        });
    }
}
