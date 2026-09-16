using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private readonly IScrapeClient _client;
    private readonly IScrapeStore _store;
    private readonly IEnumerable<IItemPageParser> _itemPageParsers;

    public ListingRefreshService(
        IScrapeClient client,
        IScrapeStore store,
        IEnumerable<IItemPageParser> itemPageParsers)
    {
        _client = client;
        _store = store;
        _itemPageParsers = itemPageParsers;
    }

    public Task RefreshActiveListings(CancellationToken ct)
    {
        _ = _client;
        _ = _store;
        _ = _itemPageParsers;
        _ = ct;
        throw new NotImplementedException();
    }
}
