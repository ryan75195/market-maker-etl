using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private readonly IScrapeClient _client;
    private readonly IScrapeStore _store;
    private readonly IItemPageParser _itemPageParser;

    public ListingRefreshService(
        IScrapeClient client,
        IScrapeStore store,
        IItemPageParser itemPageParser)
    {
        _client = client;
        _store = store;
        _itemPageParser = itemPageParser;
    }

    public Task RefreshActiveListings(CancellationToken ct)
    {
        _ = _client;
        _ = _store;
        _ = _itemPageParser;
        _ = ct;
        throw new NotImplementedException();
    }
}
