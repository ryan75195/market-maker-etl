using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class ItemDetailFetchService : IItemDetailFetchService
{
    private const string DetailPhase = "Detail";
    private const string FetchFailedIssueType = "ItemDetailFetchFailed";
    private const string ParseFailedIssueType = "ItemDetailParseFailed";

    private readonly IItemDetailStore _store;
    private readonly IScrapeClient _client;
    private readonly IEnumerable<IItemPageParser> _parsers;
    private readonly DetailFetchOptions _options;

    public ItemDetailFetchService(
        IItemDetailStore store,
        IScrapeClient client,
        IEnumerable<IItemPageParser> parsers,
        DetailFetchOptions options)
    {
        _store = store;
        _client = client;
        _parsers = parsers;
        _options = options;
    }

    public async Task<IReadOnlyList<ScrapeRunIssueDetails>> FetchDetails(int jobId, CancellationToken ct)
    {
        var targets = await _store.GetListingsNeedingDetail(
            jobId, _options.MaxDetailFetchesPerRun, _options.MaxDetailFetchAttempts, ct);

        if (targets.Count == 0)
        {
            return [];
        }

        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentDetailFetches));
        var results = await Task.WhenAll(targets.Select(target => FetchOne(target, gate, ct)));
        return results.Where(issue => issue is not null).Select(issue => issue!).ToList();
    }

    public async Task ApplyBackfilledDetails(
        int jobId, IReadOnlyDictionary<string, ItemPageListing> detailsByListingId, CancellationToken ct)
    {
        if (detailsByListingId.Count == 0)
        {
            return;
        }

        var entityIds = await _store.GetListingEntityIds(jobId, detailsByListingId.Keys.ToList(), ct);

        foreach (var (listingId, entityId) in entityIds)
        {
            await _store.ApplyItemDetail(entityId, detailsByListingId[listingId], ct);
        }
    }

    private async Task<ScrapeRunIssueDetails?> FetchOne(ListingDetailTarget target, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await FetchDetail(target, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ScrapeRunIssueDetails?> FetchDetail(ListingDetailTarget target, CancellationToken ct)
    {
        var parser = FindParser(target.Marketplace);

        if (string.IsNullOrWhiteSpace(target.Url) || parser is null)
        {
            await _store.MarkDetailFetchFailed(target.Id, _options.MaxDetailFetchAttempts, ct);
            return new ScrapeRunIssueDetails(
                target.ListingId, FetchFailedIssueType, "No item page URL or supported parser for this marketplace.", DetailPhase, null);
        }

        try
        {
            var html = await _client.GetPageHtml(target.Url, ct);
            var page = parser.Parse(html);

            if (page is null || string.IsNullOrWhiteSpace(page.Title))
            {
                await _store.MarkDetailFetchFailed(target.Id, _options.MaxDetailFetchAttempts, ct);
                return new ScrapeRunIssueDetails(
                    target.ListingId, ParseFailedIssueType, "Item page did not contain a parsable title.", DetailPhase, null);
            }

            await _store.ApplyItemDetail(target.Id, page, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.MarkDetailFetchFailed(target.Id, _options.MaxDetailFetchAttempts, ct);
            return new ScrapeRunIssueDetails(
                target.ListingId, FetchFailedIssueType, ExceptionIssueMessageFormatter.Describe(ex), DetailPhase, null);
        }
    }

    private IItemPageParser? FindParser(Marketplace marketplace)
    {
        foreach (var parser in _parsers)
        {
            if (parser.Marketplace == marketplace)
            {
                return parser;
            }
        }

        return null;
    }
}
