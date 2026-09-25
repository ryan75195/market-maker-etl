using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeRunIssueViewExposesItsSeverityTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private IDbContextFactory<EtlDbContext> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-run-severity-view-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        using var db = _factory.CreateDbContext();
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
    public async Task Should_label_an_informational_issue_as_info_and_a_failure_issue_as_error()
    {
        var jobId = 9;
        var runId = await SeedRun(_factory, jobId);
        var store = new ScrapeRunReportStore(_factory);
        await store.RecordIssue(
            runId,
            new ScrapeRunIssueDetails(null, "PriceBandCapHit", "hit the cap", "Search", null),
            CancellationToken.None);
        await store.RecordIssue(
            runId,
            new ScrapeRunIssueDetails(null, "SearchPageFailed", "band lost", "Search", null),
            CancellationToken.None);

        var runs = await store.GetRunsForJob(jobId, CancellationToken.None);

        var issues = runs.Single().Issues;
        var capHitIssue = issues.Single(issue => issue.IssueType == "PriceBandCapHit");
        var failedIssue = issues.Single(issue => issue.IssueType == "SearchPageFailed");
        Assert.Multiple(() =>
        {
            Assert.That(capHitIssue.Severity, Is.EqualTo("info"));
            Assert.That(failedIssue.Severity, Is.EqualTo("error"));
        });
    }

    private static async Task<int> SeedRun(IDbContextFactory<EtlDbContext> factory, int jobId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = new ScrapeRunEntity
        {
            JobId = jobId,
            SearchTerm = "ps5",
            Status = nameof(ScrapeRunStatus.Completed),
            TriggerType = nameof(TriggerType.Manual),
            StartedUtc = DateTime.UtcNow
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }
}
