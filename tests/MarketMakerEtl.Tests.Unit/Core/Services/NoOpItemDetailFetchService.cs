using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

internal sealed class NoOpItemDetailFetchService : IItemDetailFetchService
{
    public Task FetchDetails(int jobId, CancellationToken ct) => Task.CompletedTask;
}
