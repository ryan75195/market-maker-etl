using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private readonly IScrapeClient _client;
    private readonly IScrapeStore _store;

    public ListingRefreshService(IScrapeClient client, IScrapeStore store)
    {
        _client = client;
        _store = store;
    }

    public Task RefreshActiveListings(CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(_client);
        ArgumentNullException.ThrowIfNull(_store);
        ct.ThrowIfCancellationRequested();
        throw new NotImplementedException();
    }
}
