using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

internal static class ScrapeRunCompletion
{
    public static async Task<ScrapeRunStatus> DetermineStatus(EtlDbContext db, int runId, CancellationToken ct)
    {
        var issueTypes = await db.ScrapeRunIssues
            .Where(issue => issue.ScrapeRunId == runId)
            .Select(issue => issue.IssueType)
            .ToListAsync(ct);

        var hasFailure = issueTypes.Any(ScrapeRunIssueSeverityClassifier.IsFailure);
        return hasFailure ? ScrapeRunStatus.CompletedWithErrors : ScrapeRunStatus.Completed;
    }

    public static void Apply(ScrapeRunEntity run, ScrapeRunStatus status, RunCompletionCounts counts)
    {
        run.Status = status.ToString();
        run.ListingsAddedActive = counts.ListingsAddedActive;
        run.ListingsAddedSold = counts.ListingsAddedSold;
        run.ListingsUpdated = counts.ListingsUpdated;
        run.ListingsSkipped = counts.ListingsSkipped;
        run.ListingsFailed = counts.ListingsFailed;
        run.TotalListingsFound = counts.TotalListingsFound;
        run.TotalReportedBySearch = counts.TotalReportedBySearch;
        run.BackfillItemPageFetches = counts.BackfillItemPageFetches;
        run.SearchCompletedUtc = counts.SearchCompletedUtc;
        run.DetailCompletedUtc = counts.DetailCompletedUtc;
        run.CompletedUtc = DateTime.UtcNow;
    }
}
