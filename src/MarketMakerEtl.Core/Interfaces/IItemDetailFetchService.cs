namespace MarketMakerEtl.Core.Interfaces;

public interface IItemDetailFetchService
{
    Task FetchDetails(int jobId, CancellationToken ct);
}
