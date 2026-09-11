using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshService : IListingRefreshService
{
    private const string ActiveStatus = "Active";

    private readonly IScrapeClient _client;
    private readonly IScrapeStore _store;

    public ListingRefreshService(IScrapeClient client, IScrapeStore store)
    {
        _client = client;
        _store = store;
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
        var page = EbayItemPageParser.Parse(html);

        if (page?.Status is null)
        {
            return;
        }

        if (HasStatusChanged(target.ItemStatus, page.Status))
        {
            await _store.RecordStatusChange(target.Id, page.Status, page.Price, ct);
        }
    }

    private static bool HasStatusChanged(string? stored, string observed) =>
        !string.Equals(Normalise(stored), observed, StringComparison.Ordinal);

    private static string Normalise(string? status) =>
        string.IsNullOrWhiteSpace(status) ? ActiveStatus : status;
}
