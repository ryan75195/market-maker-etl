using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeRunRecordsItsTriggerTypeTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-trigger-{Guid.NewGuid():N}.db");
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

    [TestCase(TriggerType.Manual)]
    [TestCase(TriggerType.Scheduled)]
    public async Task Should_record_the_trigger_type_a_run_was_enqueued_with(TriggerType trigger)
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);

        var runId = await store.EnqueueRun(jobId, "ps5", trigger, CancellationToken.None);

        var run = await store.GetRun(runId, CancellationToken.None);
        Assert.That(run!.TriggerType, Is.EqualTo(trigger));
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
