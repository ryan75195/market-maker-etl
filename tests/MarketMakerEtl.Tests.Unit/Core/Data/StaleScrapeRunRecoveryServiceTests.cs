using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class StaleScrapeRunRecoveryServiceTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-{Guid.NewGuid():N}.db");
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
    public async Task Should_fail_a_run_left_running_by_a_previous_process()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);
        await store.ClaimNextQueuedRun(CancellationToken.None);

        var failed = await CreateRecovery().FailRunsLeftRunning(CancellationToken.None);

        var run = await store.GetRun(runId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(run!.Status, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(run.ErrorMessage, Does.Contain("previous ETL process"));
        });
    }

    [Test]
    public async Task Should_leave_queued_and_completed_runs_untouched()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);

        var completedRunId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);
        await store.ClaimNextQueuedRun(CancellationToken.None);
        await store.CompleteRun(
            completedRunId,
            new RunCompletionCounts(0, 0, 0, 0, 0, 0, 0, DateTime.UtcNow, DateTime.UtcNow),
            CancellationToken.None);

        var queuedRunId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);

        var failed = await CreateRecovery().FailRunsLeftRunning(CancellationToken.None);

        var queuedRun = await store.GetRun(queuedRunId, CancellationToken.None);
        var completedRun = await store.GetRun(completedRunId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(queuedRun!.Status, Is.EqualTo(ScrapeRunStatus.Queued));
            Assert.That(completedRun!.Status, Is.EqualTo(ScrapeRunStatus.Completed));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private StaleScrapeRunRecoveryService CreateRecovery() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
