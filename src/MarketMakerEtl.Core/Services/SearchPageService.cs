using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class SearchPageService : ISearchPageService
{
    private const string ListingCardSelector = "li.s-card, li.s-item, a[href*='/itm/']";

    private static readonly HtmlParser DocumentParser = new();

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
        var merged = new Dictionary<string, ListingSummary>(StringComparer.Ordinal);

        await CollectDirection(searchTerm, sold: false, merged, ct);

        if (_options.CollectSold)
        {
            await CollectDirection(searchTerm, sold: true, merged, ct);
        }

        return merged.Values.ToList();
    }

    private async Task CollectDirection(
        string searchTerm,
        bool sold,
        Dictionary<string, ListingSummary> merged,
        CancellationToken ct)
    {
        for (var page = 1; page <= _options.MaxPages; page++)
        {
            var url = _urls.BuildSearch(searchTerm, sold, page);
            var html = await _client.GetPageHtml(url, ct);
            var pageResults = _parser.Parse(html);

            if (pageResults.Count == 0)
            {
                if (ContainsListingMarkup(html))
                {
                    throw new InvalidOperationException(
                        $"Search page '{url}' contained listing markup but parsed to zero results.");
                }

                return;
            }

            foreach (var listing in pageResults)
            {
                merged[listing.ListingId] = listing;
            }
        }
    }

    private static bool ContainsListingMarkup(string html)
    {
        var document = DocumentParser.ParseDocument(html);
        return document.QuerySelector(ListingCardSelector) is not null;
    }
}
