using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class SearchPageService : ISearchPageService
{
    private readonly IScrapeClient _client;
    private readonly IEbaySearchUrlService _urls;
    private readonly ISearchPageParser _parser;
    private readonly ScrapeOptions _options;

    public SearchPageService(
        IScrapeClient client,
        IEbaySearchUrlService urls,
        ISearchPageParser parser,
        ScrapeOptions options)
    {
        _client = client;
        _urls = urls;
        _parser = parser;
        _options = options;
    }

    public async Task<IReadOnlyList<ListingSummary>> Collect(string searchTerm, CancellationToken ct)
    {
        var results = new List<ListingSummary>();
        await CollectDirection(searchTerm, sold: false, results, ct);

        if (_options.CollectSold)
        {
            await CollectDirection(searchTerm, sold: true, results, ct);
        }

        return results;
    }

    private async Task CollectDirection(
        string searchTerm,
        bool sold,
        List<ListingSummary> results,
        CancellationToken ct)
    {
        for (var page = 1; page <= _options.MaxPages; page++)
        {
            var url = _urls.BuildSearch(searchTerm, sold, page);
            var html = await _client.GetPageHtml(url, ct);
            var pageResults = _parser.Parse(html);

            if (pageResults.Count == 0)
            {
                return;
            }

            results.AddRange(pageResults);
        }
    }
}
