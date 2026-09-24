using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

internal static class ScrapeRunViewMapper
{
    public static async Task<ScrapeRunView> Build(EtlDbContext db, ScrapeRunEntity run, CancellationToken ct)
    {
        var issues = await db.ScrapeRunIssues
            .Where(issue => issue.ScrapeRunId == run.Id)
            .OrderBy(issue => issue.Id)
            .ToListAsync(ct);

        return new ScrapeRunView(
            run.Id,
            run.JobId,
            run.SearchTerm,
            Enum.Parse<ScrapeRunStatus>(run.Status),
            run.ErrorMessage,
            Enum.Parse<TriggerType>(run.TriggerType),
            run.ListingsAddedActive,
            run.ListingsAddedSold,
            run.ListingsUpdated,
            run.ListingsSkipped,
            run.ListingsFailed,
            run.TotalListingsFound,
            run.TotalReportedBySearch,
            run.StartedUtc,
            run.SearchCompletedUtc,
            run.DetailCompletedUtc,
            run.CompletedUtc,
            issues.Select(MapIssue).ToList());
    }

    private static ScrapeRunIssueView MapIssue(ScrapeRunIssueEntity issue) =>
        new(
            issue.Id,
            issue.ScrapeRunId,
            issue.ListingId,
            issue.IssueType,
            issue.ErrorMessage,
            issue.Phase,
            issue.HttpStatusCode,
            issue.CreatedUtc);
}
