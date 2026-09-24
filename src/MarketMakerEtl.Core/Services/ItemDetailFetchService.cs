using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class ItemDetailFetchService : IItemDetailFetchService
{
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

    public async Task FetchDetails(int jobId, CancellationToken ct)
    {
        var targets = await _store.GetListingsNeedingDetail(jobId, _options.MaxDetailFetchesPerRun, ct);

        if (targets.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentDetailFetches));
        await Task.WhenAll(targets.Select(target => FetchOne(target, gate, ct)));
    }

    private async Task FetchOne(ListingDetailTarget target, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await FetchDetail(target, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task FetchDetail(ListingDetailTarget target, CancellationToken ct)
    {
        var parser = FindParser(target.Marketplace);

        if (string.IsNullOrWhiteSpace(target.Url) || parser is null)
        {
            await _store.MarkDetailFetchFailed(target.Id, ct);
            return;
        }

        try
        {
            var html = await _client.GetPageHtml(target.Url, ct);
            var page = parser.Parse(html);

            if (page is null || string.IsNullOrWhiteSpace(page.Title))
            {
                await _store.MarkDetailFetchFailed(target.Id, ct);
                return;
            }

            await _store.ApplyItemDetail(target.Id, page, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.MarkDetailFetchFailed(target.Id, ct);
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
