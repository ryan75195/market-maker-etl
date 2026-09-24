using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class ScrapeRunService : IScrapeRunService
{
    private readonly ISearchPageService _search;
    private readonly IScrapeStore _store;
    private readonly IItemDetailFetchService _detailFetch;
    private readonly IScrapeRunReportStore _reports;

    public ScrapeRunService(
        ISearchPageService search,
        IScrapeStore store,
        IItemDetailFetchService detailFetch,
        IScrapeRunReportStore reports)
    {
        _search = search;
        _store = store;
        _detailFetch = detailFetch;
        _reports = reports;
    }

    public async Task Run(ScrapeRunWork work, CancellationToken ct)
    {
        try
        {
            var existingListings = await _store.GetListings(work.JobId, ct);
            var knownSoldListingIds = existingListings
                .Where(listing => listing.IsSold)
                .Select(listing => listing.ListingId)
                .ToHashSet(StringComparer.Ordinal);

            var result = await _search.Collect(work.SearchTerm, work.Marketplace, knownSoldListingIds, ct);
            var searchCompletedUtc = DateTime.UtcNow;

            var upsertSummary = await _store.UpsertListings(work.JobId, result.Listings, ct);

            var detailIssues = await _detailFetch.FetchDetails(work.JobId, ct);
            var detailCompletedUtc = DateTime.UtcNow;

            foreach (var issue in result.Issues.Concat(detailIssues))
            {
                await _reports.RecordIssue(work.RunId, issue, ct);
            }

            var counts = new RunCompletionCounts(
                upsertSummary.AddedActive,
                upsertSummary.AddedSold,
                upsertSummary.Updated,
                upsertSummary.Skipped,
                ListingsFailed: detailIssues.Count(issue => issue.ListingId is not null),
                TotalListingsFound: result.Listings.Count,
                TotalReportedBySearch: result.TotalReportedBySearch,
                SearchCompletedUtc: searchCompletedUtc,
                DetailCompletedUtc: detailCompletedUtc,
                BackfillItemPageFetches: result.BackfillItemPageFetches);

            await _store.CompleteRun(work.RunId, counts, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.FailRun(work.RunId, ex.Message, ct);
        }
    }
}
