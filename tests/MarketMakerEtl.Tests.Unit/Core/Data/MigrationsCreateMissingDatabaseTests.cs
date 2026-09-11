using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigrationsCreateMissingDatabaseTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-create-{Guid.NewGuid():N}.db");
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
    public async Task Should_create_a_missing_database_with_the_migrated_schema()
    {
        Assert.That(File.Exists(_databasePath), Is.False);
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await factory.ApplyMigrations(CancellationToken.None);

        IReadOnlyList<string> applied;
        await using (var db = await factory.CreateDbContextAsync())
        {
            applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        }

        var store = new ScrapeStore(factory, new ScrapeRunStateService());
        var jobId = await store.EnsureJob("new-database-search", CancellationToken.None);
        var listing = new ListingSummary("111111111111", "Fresh PS5", 100m, "GBP", "https://x/itm/1", false);
        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(_databasePath), Is.True);
            Assert.That(applied, Is.Not.Empty);
            Assert.That(listings, Has.Count.EqualTo(1));
            Assert.That(listings[0].Title, Is.EqualTo("Fresh PS5"));
            Assert.That(listings[0].Price, Is.EqualTo(100m));
        });
    }
}
