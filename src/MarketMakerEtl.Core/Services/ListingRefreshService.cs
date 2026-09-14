using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";

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

        var html = await _client.GetPageHtml(target.Url, ct);
        var page = _itemPageParser.Parse(html);

        if (page is null)
        {
            return;
        }

        var status = page.Status;

        if (status is null || string.IsNullOrWhiteSpace(page.Title))
        {
            return;
        }

        if (HasStatusChanged(target.ItemStatus, status))
        {
            await _store.RecordStatusChange(target.Id, ToObservation(status, page), ct);
        }
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
