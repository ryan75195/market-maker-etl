using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class AnUnknownIssueTypeFailsSafeToCompletedWithErrorsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private IDbContextFactory<EtlDbContext> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-run-unknown-{Guid.NewGuid():N}.db");
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
    public async Task Should_treat_an_unrecognised_issue_type_as_a_failure()
    {
        var runId = await SeedRun(_factory);
        await SeedIssue(_factory, runId, "SomeFutureIssueTypeNotYetClassified");

        await using var db = await _factory.CreateDbContextAsync();
        var status = await ScrapeRunCompletion.DetermineStatus(db, runId, CancellationToken.None);

        Assert.That(status, Is.EqualTo(ScrapeRunStatus.CompletedWithErrors));
    }

    private static async Task<int> SeedRun(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = new ScrapeRunEntity
        {
            JobId = 1,
            SearchTerm = "ps5",
            Status = nameof(ScrapeRunStatus.Running),
            TriggerType = nameof(TriggerType.Manual),
            StartedUtc = DateTime.UtcNow
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private static async Task SeedIssue(IDbContextFactory<EtlDbContext> factory, int runId, string issueType)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.ScrapeRunIssues.Add(new ScrapeRunIssueEntity
        {
            ScrapeRunId = runId,
            IssueType = issueType,
            ErrorMessage = "n/a",
            Phase = "Search",
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
