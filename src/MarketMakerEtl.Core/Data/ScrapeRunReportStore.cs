using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ScrapeRunReportStore : IScrapeRunReportStore
{
    private const int MaxRecentRuns = 20;

    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ScrapeRunReportStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task RecordIssue(int runId, ScrapeRunIssueDetails issue, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ScrapeRunIssues.Add(new ScrapeRunIssueEntity
        {
            ScrapeRunId = runId,
            ListingId = issue.ListingId,
            IssueType = issue.IssueType,
            ErrorMessage = issue.ErrorMessage,
            Phase = issue.Phase,
            HttpStatusCode = issue.HttpStatusCode,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ScrapeRunView>> GetRunsForJob(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var runs = await db.ScrapeRuns
            .Where(run => run.JobId == jobId)
            .OrderByDescending(run => run.Id)
            .Take(MaxRecentRuns)
            .ToListAsync(ct);

        var views = new List<ScrapeRunView>();
        foreach (var run in runs)
        {
            views.Add(await ScrapeRunViewMapper.Build(db, run, ct));
        }

        return views;
    }

    public async Task<JobLastRunView?> GetLastRun(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var latest = await db.ScrapeRuns
            .Where(run => run.JobId == jobId)
            .OrderByDescending(run => run.Id)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return null;
        }

        var lastCompletedRunUtc = await db.ScrapeRuns
            .Where(run => run.JobId == jobId && run.CompletedUtc != null)
            .OrderByDescending(run => run.CompletedUtc)
            .Select(run => (DateTime?)run.CompletedUtc)
            .FirstOrDefaultAsync(ct);

        return new JobLastRunView(
            Enum.Parse<ScrapeRunStatus>(latest.Status), latest.StartedUtc, latest.CompletedUtc, lastCompletedRunUtc);
    }
}
