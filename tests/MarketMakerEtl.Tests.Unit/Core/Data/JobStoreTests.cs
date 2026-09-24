using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class JobStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-job-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_and_return_a_created_job_with_every_field()
    {
        var store = CreateStore();

        var job = await store.CreateJob(
            new JobDetails("nintendo switch", Marketplace.Mercari, "OLED only", 8, true, []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(job.SearchTerm, Is.EqualTo("nintendo switch"));
            Assert.That(job.Marketplace, Is.EqualTo(Marketplace.Mercari));
            Assert.That(job.FilterInstructions, Is.EqualTo("OLED only"));
            Assert.That(job.IntervalHours, Is.EqualTo(8));
            Assert.That(job.IsEnabled, Is.True);
            Assert.That(job.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task Should_list_every_created_job()
    {
        var store = CreateStore();
        await store.CreateJob(BuildDetails("job one"), CancellationToken.None);
        await store.CreateJob(BuildDetails("job two"), CancellationToken.None);

        var jobs = await store.GetJobs(CancellationToken.None);

        Assert.That(jobs.Select(j => j.SearchTerm), Is.EquivalentTo(new[] { "job one", "job two" }));
    }

    [Test]
    public async Task Should_return_a_single_job_by_id()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("solo job"), CancellationToken.None);

        var job = await store.GetJob(created.Id, CancellationToken.None);

        Assert.That(job!.SearchTerm, Is.EqualTo("solo job"));
    }

    [Test]
    public async Task Should_update_a_jobs_editable_fields()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("before"), CancellationToken.None);

        var updated = await store.UpdateJob(
            created.Id,
            new JobDetails("after", Marketplace.Ebay, "changed", 48, false, []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated!.SearchTerm, Is.EqualTo("after"));
            Assert.That(updated.Marketplace, Is.EqualTo(Marketplace.Ebay));
            Assert.That(updated.FilterInstructions, Is.EqualTo("changed"));
            Assert.That(updated.IntervalHours, Is.EqualTo(48));
            Assert.That(updated.IsEnabled, Is.False);
        });
    }

    [Test]
    public async Task Should_delete_a_job_and_no_longer_return_it()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("to delete"), CancellationToken.None);

        var deleted = await store.DeleteJob(created.Id, CancellationToken.None);
        var afterDelete = await store.GetJob(created.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(afterDelete, Is.Null);
        });
    }

    [Test]
    public async Task Should_toggle_a_jobs_enabled_flag()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("toggle me"), CancellationToken.None);

        var disabled = await store.SetJobEnabled(created.Id, false, CancellationToken.None);
        var enabled = await store.SetJobEnabled(created.Id, true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(enabled!.IsEnabled, Is.True);
        });
    }

    [Test]
    public async Task Should_replace_a_jobs_categories()
    {
        var store = CreateStore();
        var categoryStore = CreateCategoryStore();
        var created = await store.CreateJob(BuildDetails("categorised job"), CancellationToken.None);
        var category = await categoryStore.CreateCategory("Collectibles", true, CancellationToken.None);

        var assigned = await store.SetJobCategories(created.Id, [category.Id], CancellationToken.None);

        Assert.That(assigned!.Categories.Select(c => c.Name), Is.EquivalentTo(new[] { "Collectibles" }));
    }

    [Test]
    public async Task Should_stamp_a_job_as_queued()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("queue me"), CancellationToken.None);

        var queuedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var queued = await store.MarkQueued(created.Id, queuedUtc, CancellationToken.None);

        Assert.That(queued!.LastQueuedUtc, Is.EqualTo(queuedUtc));
    }

    [Test]
    public async Task Should_return_only_effectively_enabled_jobs()
    {
        var store = CreateStore();
        var categoryStore = CreateCategoryStore();
        var disabledJob = await store.CreateJob(BuildDetails("disabled job", isEnabled: false), CancellationToken.None);
        var enabledJob = await store.CreateJob(BuildDetails("enabled job"), CancellationToken.None);
        var disabledCategory = await categoryStore.CreateCategory("Disabled Category", false, CancellationToken.None);
        await store.SetJobCategories(disabledJob.Id, [disabledCategory.Id], CancellationToken.None);

        var effectivelyEnabled = await store.GetEffectivelyEnabledJobs(CancellationToken.None);

        Assert.That(effectivelyEnabled.Select(j => j.Id), Is.EquivalentTo(new[] { enabledJob.Id }));
    }

    [Test]
    public async Task Should_report_no_active_run_when_none_exists()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("no active run"), CancellationToken.None);

        var hasActiveRun = await store.HasQueuedOrRunningRun(created.Id, CancellationToken.None);

        Assert.That(hasActiveRun, Is.False);
    }

    [Test]
    public async Task Should_report_an_active_run_when_queued_or_running()
    {
        var store = CreateStore();
        var created = await store.CreateJob(BuildDetails("active run"), CancellationToken.None);
        var scrapeStore = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var runId = await scrapeStore.EnqueueRun(created.Id, created.SearchTerm, TriggerType.Manual, CancellationToken.None);

        var hasQueuedRun = await store.HasQueuedOrRunningRun(created.Id, CancellationToken.None);
        await scrapeStore.ClaimNextQueuedRun(CancellationToken.None);
        var hasRunningRun = await store.HasQueuedOrRunningRun(created.Id, CancellationToken.None);
        await scrapeStore.CompleteRun(
            runId,
            new RunCompletionCounts(0, 0, 0, 0, 0, 0, null, DateTime.UtcNow, DateTime.UtcNow),
            CancellationToken.None);
        var hasRunAfterCompletion = await store.HasQueuedOrRunningRun(created.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(hasQueuedRun, Is.True);
            Assert.That(hasRunningRun, Is.True);
            Assert.That(hasRunAfterCompletion, Is.False);
        });
    }

    private JobStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private CategoryStore CreateCategoryStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private static JobDetails BuildDetails(string searchTerm, bool isEnabled = true) =>
        new(searchTerm, Marketplace.Mercari, null, 24, isEnabled, []);
}
