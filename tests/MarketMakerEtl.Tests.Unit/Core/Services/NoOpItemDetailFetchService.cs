using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

internal sealed class NoOpItemDetailFetchService : IItemDetailFetchService
{
    public Task<IReadOnlyList<ScrapeRunIssueDetails>> FetchDetails(int jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScrapeRunIssueDetails>>([]);

    public Task ApplyBackfilledDetails(
        int jobId, IReadOnlyDictionary<string, ItemPageListing> detailsByListingId, CancellationToken ct) =>
        Task.CompletedTask;
}
