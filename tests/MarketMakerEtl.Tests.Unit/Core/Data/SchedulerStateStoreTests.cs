using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SchedulerStateStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-scheduler-state-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
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
    public async Task Should_return_null_when_no_refresh_has_run()
    {
        var store = CreateStore();

        var lastRefresh = await store.GetLastListingRefreshUtc(CancellationToken.None);

        Assert.That(lastRefresh, Is.Null);
    }

    [Test]
    public async Task Should_persist_and_return_the_last_refresh_time()
    {
        var store = CreateStore();
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await store.SetLastListingRefreshUtc(timestamp, CancellationToken.None);
        var lastRefresh = await store.GetLastListingRefreshUtc(CancellationToken.None);

        Assert.That(lastRefresh, Is.EqualTo(timestamp));
    }

    [Test]
    public async Task Should_overwrite_the_last_refresh_time_on_a_second_call()
    {
        var store = CreateStore();
        var firstTimestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var secondTimestamp = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);

        await store.SetLastListingRefreshUtc(firstTimestamp, CancellationToken.None);
        await store.SetLastListingRefreshUtc(secondTimestamp, CancellationToken.None);
        var lastRefresh = await store.GetLastListingRefreshUtc(CancellationToken.None);

        Assert.That(lastRefresh, Is.EqualTo(secondTimestamp));
    }

    private SchedulerStateStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
}
