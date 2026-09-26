using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeRunReportStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-run-report-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_an_issue_against_a_run()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var runId = await SeedRun(factory, jobId: 1);
        var store = new ScrapeRunReportStore(factory);
        var issue = new ScrapeRunIssueDetails("111111111111", "ItemDetailFetchFailed", "blocked", "Detail", 503);

        await store.RecordIssue(runId, issue, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var recorded = await db.ScrapeRunIssues.SingleAsync(i => i.ScrapeRunId == runId);
        Assert.Multiple(() =>
        {
            Assert.That(recorded.ListingId, Is.EqualTo("111111111111"));
            Assert.That(recorded.IssueType, Is.EqualTo("ItemDetailFetchFailed"));
            Assert.That(recorded.ErrorMessage, Is.EqualTo("blocked"));
            Assert.That(recorded.Phase, Is.EqualTo("Detail"));
            Assert.That(recorded.HttpStatusCode, Is.EqualTo(503));
        });
    }

    [Test]
    public async Task Should_return_recent_runs_for_a_job_with_their_recorded_issues_newest_first()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var olderRunId = await SeedRun(factory, jobId: 5);
        var newerRunId = await SeedRun(factory, jobId: 5);
        var store = new ScrapeRunReportStore(factory);
        var issue = new ScrapeRunIssueDetails(null, "PriceBandCapHit", "hit the cap", "Search", null);
        await store.RecordIssue(newerRunId, issue, CancellationToken.None);

        var runs = await store.GetRunsForJob(5, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.Count.EqualTo(2));
            Assert.That(runs[0].RunId, Is.EqualTo(newerRunId));
            Assert.That(runs[1].RunId, Is.EqualTo(olderRunId));
            Assert.That(runs[0].Issues, Has.Count.EqualTo(1));
            Assert.That(runs[0].Issues[0].IssueType, Is.EqualTo("PriceBandCapHit"));
            Assert.That(runs[1].Issues, Is.Empty);
        });
    }

    [Test]
    public async Task Should_return_null_when_the_job_has_no_runs()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ScrapeRunReportStore(factory);

        var lastRun = await store.GetLastRun(999, CancellationToken.None);

        Assert.That(lastRun, Is.Null);
    }

    [Test]
    public async Task Should_report_the_most_recently_started_run_regardless_of_its_status()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedRun(factory, jobId: 7, status: ScrapeRunStatus.Completed, completedUtc: DateTime.UtcNow.AddHours(-2));
        var latestRunId = await SeedRun(factory, jobId: 7, status: ScrapeRunStatus.Running, completedUtc: null);
        var store = new ScrapeRunReportStore(factory);

        var lastRun = await store.GetLastRun(7, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(lastRun, Is.Not.Null);
            Assert.That(lastRun!.Status, Is.EqualTo(ScrapeRunStatus.Running));
            Assert.That(lastRun.CompletedUtc, Is.Null);
        });
        Assert.That(latestRunId, Is.GreaterThan(0));
    }

    [Test]
    public async Task Should_report_the_last_completed_run_separately_from_an_in_progress_latest_run()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var completedUtc = DateTime.UtcNow.AddHours(-3);
        await SeedRun(factory, jobId: 9, status: ScrapeRunStatus.Completed, completedUtc: completedUtc);
        await SeedRun(factory, jobId: 9, status: ScrapeRunStatus.Running, completedUtc: null);
        var store = new ScrapeRunReportStore(factory);

        var lastRun = await store.GetLastRun(9, CancellationToken.None);

        Assert.That(lastRun!.LastCompletedRunUtc, Is.EqualTo(completedUtc).Within(TimeSpan.FromSeconds(1)));
    }

    private static async Task<int> SeedRun(
        IDbContextFactory<EtlDbContext> factory,
        int jobId,
        ScrapeRunStatus status = ScrapeRunStatus.Completed,
        DateTime? completedUtc = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = new ScrapeRunEntity
        {
            JobId = jobId,
            SearchTerm = "ps5",
            Status = status.ToString(),
            TriggerType = nameof(TriggerType.Manual),
            StartedUtc = DateTime.UtcNow,
            CompletedUtc = completedUtc
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }
}
