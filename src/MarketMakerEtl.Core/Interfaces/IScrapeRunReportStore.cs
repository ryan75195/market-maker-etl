using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeRunReportStore
{
    Task RecordIssue(int runId, ScrapeRunIssueDetails issue, CancellationToken ct);

    Task<IReadOnlyList<ScrapeRunView>> GetRunsForJob(int jobId, CancellationToken ct);
}
