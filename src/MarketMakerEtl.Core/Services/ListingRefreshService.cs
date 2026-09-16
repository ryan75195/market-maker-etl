using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";

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

    public async Task RefreshActiveListings(CancellationToken ct)
    {
        var targets = await _store.GetActiveListings(ct);

        foreach (var target in targets)
        {
            await RefreshListing(target, ct);
        }
    }

    private async Task RefreshListing(ListingRefreshTarget target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.Url))
        {
            return;
        }

        var parser = FindParser(target.Marketplace);
        if (parser is null)
        {
            return;
        }

        var html = await _client.GetPageHtml(target.Url, ct);
        var page = parser.Parse(html);

        if (page is null || page.Status is null || string.IsNullOrWhiteSpace(page.Title))
        {
            return;
        }

        if (HasStatusChanged(target.ItemStatus, page.Status))
        {
            await _store.RecordStatusChange(target.Id, ToObservation(page.Status, page), ct);
        }
    }

    private IItemPageParser? FindParser(Marketplace marketplace)
    {
        foreach (var parser in _itemPageParsers)
        {
            if (parser.Marketplace == marketplace)
            {
                return parser;
            }
        }

        return null;
    }

    private static ListingStatusObservation ToObservation(string status, ItemPageListing page) =>
        new(
            status,
            page.Price,
            page.SoldPrice,
            SoldDateParser.Parse(page.SoldDate),
            page.Seller,
            string.Equals(status, SoldStatus, StringComparison.Ordinal));

    private static bool HasStatusChanged(string? stored, string observed) =>
        !string.Equals(Normalise(stored), observed, StringComparison.Ordinal);

    private static string Normalise(string? status) =>
        string.IsNullOrWhiteSpace(status) ? ActiveStatus : status;
}
