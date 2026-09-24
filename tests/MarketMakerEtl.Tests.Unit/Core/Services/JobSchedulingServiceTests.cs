using Microsoft.Extensions.Time.Testing;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class JobSchedulingServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private JobStore _jobs = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-job-scheduling-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        _jobs = new JobStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
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
    public async Task Should_queue_exactly_one_run_for_a_due_enabled_job()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        await _jobs.CreateJob(BuildDetails("labubu"), CancellationToken.None);

        var queuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        var job = (await _jobs.GetJobs(CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(queuedCount, Is.EqualTo(1));
            Assert.That(job.LastQueuedUtc, Is.EqualTo(StartTime.UtcDateTime));
        });
    }

    [Test]
    public async Task Should_not_queue_a_job_before_its_interval_elapses()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        await _jobs.CreateJob(BuildDetails("labubu", intervalHours: 24), CancellationToken.None);
        await scheduling.QueueDueJobs(CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromHours(23));
        var secondQueuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(secondQueuedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Should_queue_a_job_once_its_interval_has_fully_elapsed()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        await _jobs.CreateJob(BuildDetails("labubu", intervalHours: 24), CancellationToken.None);
        await scheduling.QueueDueJobs(CancellationToken.None);
        var scrapeStore = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var work = await scrapeStore.ClaimNextQueuedRun(CancellationToken.None);
        await scrapeStore.CompleteRun(
            work!.RunId,
            new RunCompletionCounts(0, 0, 0, 0, 0, 0, null, DateTime.UtcNow, DateTime.UtcNow),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromHours(24));
        var secondQueuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(secondQueuedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Should_not_queue_a_disabled_job()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        await _jobs.CreateJob(BuildDetails("labubu", isEnabled: false), CancellationToken.None);

        var queuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(queuedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Should_not_queue_a_job_disabled_by_its_categories()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        var categoryStore = new CategoryStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
        var category = await categoryStore.CreateCategory("Disabled", false, CancellationToken.None);
        var job = await _jobs.CreateJob(BuildDetails("labubu"), CancellationToken.None);
        await _jobs.SetJobCategories(job.Id, [category.Id], CancellationToken.None);

        var queuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(queuedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Should_not_queue_a_job_with_an_existing_queued_run()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateScheduling(timeProvider);
        var job = await _jobs.CreateJob(BuildDetails("labubu"), CancellationToken.None);
        var scrapeStore = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        await scrapeStore.EnqueueRun(job.Id, job.SearchTerm, TriggerType.Manual, CancellationToken.None);

        var queuedCount = await scheduling.QueueDueJobs(CancellationToken.None);

        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var runCount = await db.ScrapeRuns.CountAsync(r => r.JobId == job.Id);
        Assert.Multiple(() =>
        {
            Assert.That(queuedCount, Is.EqualTo(0));
            Assert.That(runCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_not_requeue_a_job_across_a_simulated_restart_within_its_interval()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var firstInstanceScheduling = CreateScheduling(timeProvider);
        await _jobs.CreateJob(BuildDetails("labubu", intervalHours: 24), CancellationToken.None);
        await firstInstanceScheduling.QueueDueJobs(CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromHours(1));
        var secondInstanceScheduling = CreateScheduling(timeProvider);
        var queuedAfterRestart = await secondInstanceScheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(queuedAfterRestart, Is.EqualTo(0));
    }

    private JobSchedulingService CreateScheduling(TimeProvider timeProvider)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var scrapeStore = new ScrapeStore(factory, new ScrapeRunStateService());
        return new JobSchedulingService(_jobs, scrapeStore, timeProvider);
    }

    private static JobDetails BuildDetails(string searchTerm, bool isEnabled = true, int intervalHours = 24) =>
        new(searchTerm, Marketplace.Mercari, null, intervalHours, isEnabled, []);
}
