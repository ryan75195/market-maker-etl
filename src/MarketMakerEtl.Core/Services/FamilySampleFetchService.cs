using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilySampleFetchService : IFamilySampleFetchService
{
    private const int MaxDetailConcurrency = 5;
    private const int SearchPagesPerStatus = 2;
    private const int SearchOffsetStep = 100;

    private readonly IScrapeClient _client;
    private readonly MarketplaceAdapters _adapters;
    private readonly OnboardingOptions _options;

    public FamilySampleFetchService(IScrapeClient client, MarketplaceAdapters adapters, OnboardingOptions options)
    {
        _client = client;
        _adapters = adapters;
        _options = options;
    }

    public async Task<IReadOnlyList<FamilySampleListing>> FetchSample(string searchTerm, CancellationToken ct)
    {
        var pooled = await FetchSearchPool(searchTerm, ct);
        var candidates = SelectStratifiedSample(pooled);
        if (candidates.Count == 0)
        {
            return [];
        }

        var parser = _adapters.ItemParsers.FirstOrDefault(p => p.Marketplace == Marketplace.Mercari);
        using var gate = new SemaphoreSlim(MaxDetailConcurrency);
        var results = await Task.WhenAll(candidates.Select(listing => FetchOne(listing, parser, gate, ct)));
        return results.ToList();
    }

    private async Task<IReadOnlyList<ListingSummary>> FetchSearchPool(string searchTerm, CancellationToken ct)
    {
        var urls = SelectUrlService();
        var parser = SelectSearchParser();
        var pooled = new Dictionary<string, ListingSummary>(StringComparer.Ordinal);

        foreach (var sold in new[] { false, true })
        {
            for (var page = 0; page < SearchPagesPerStatus; page++)
            {
                await FetchSearchPage(searchTerm, sold, page * SearchOffsetStep, urls, parser, pooled, ct);
            }
        }

        return pooled.Values.ToList();
    }

    private async Task FetchSearchPage(
        string searchTerm,
        bool sold,
        int offset,
        IPriceBandSearchUrlService urls,
        ISearchPageParser parser,
        Dictionary<string, ListingSummary> pooled,
        CancellationToken ct)
    {
        var url = urls.BuildSearch(searchTerm, sold, minPrice: null, maxPrice: null, offset);
        var html = await _client.GetPageHtml(url, ct);
        var pageResult = parser.Parse(html);
        foreach (var listing in pageResult.Listings)
        {
            pooled[listing.ListingId] = listing;
        }
    }

    private IPriceBandSearchUrlService SelectUrlService() =>
        _adapters.UrlServices.SingleOrDefault(service => service.Marketplace == Marketplace.Mercari)
            as IPriceBandSearchUrlService
        ?? throw new InvalidOperationException(
            "Family sample fetch requires a Mercari search URL service that supports price-band offsets.");

    private ISearchPageParser SelectSearchParser() =>
        _adapters.SearchParsers.SingleOrDefault(parser => parser.Marketplace == Marketplace.Mercari)
        ?? throw new InvalidOperationException("No search parser registered for Mercari.");

    private IReadOnlyList<ListingSummary> SelectStratifiedSample(IReadOnlyList<ListingSummary> pooled)
    {
        var active = pooled.Where(listing => !listing.IsSold).OrderBy(PriceKey).ToList();
        var sold = pooled.Where(listing => listing.IsSold).OrderBy(PriceKey).ToList();

        var activeTarget = _options.MaxSampleListings / 2;
        var soldTarget = _options.MaxSampleListings - activeTarget;
        var activeSample = PickEvenlySpaced(active, activeTarget);
        var soldSample = PickEvenlySpaced(sold, soldTarget);

        var shortfall = _options.MaxSampleListings - activeSample.Count - soldSample.Count;
        if (shortfall <= 0)
        {
            return activeSample.Concat(soldSample).ToList();
        }

        var activeLeftover = active.Except(activeSample).ToList();
        var soldLeftover = sold.Except(soldSample).ToList();
        var backfillSource = activeLeftover.Count >= soldLeftover.Count ? activeLeftover : soldLeftover;
        var backfill = PickEvenlySpaced(backfillSource, shortfall);
        return activeSample.Concat(soldSample).Concat(backfill).ToList();
    }

    private static decimal PriceKey(ListingSummary listing) => listing.Price ?? decimal.MaxValue;

    private static IReadOnlyList<ListingSummary> PickEvenlySpaced(IReadOnlyList<ListingSummary> sorted, int take)
    {
        if (take <= 0 || sorted.Count == 0)
        {
            return [];
        }

        if (sorted.Count <= take)
        {
            return sorted;
        }

        var lastIndex = sorted.Count - 1;
        var divisor = Math.Max(take - 1, 1);
        var usedIndices = new HashSet<int>();
        var picked = new List<ListingSummary>(take);
        for (var i = 0; i < take; i++)
        {
            var index = (int)Math.Round(i * (double)lastIndex / divisor);
            while (!usedIndices.Add(index) && index < lastIndex)
            {
                index++;
            }

            picked.Add(sorted[index]);
        }

        return picked;
    }

    private async Task<FamilySampleListing> FetchOne(
        ListingSummary listing, IItemPageParser? parser, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var description = await TryFetchDescription(listing, parser, ct);
            return BuildSampleListing(listing, Truncate(description ?? listing.Title));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string?> TryFetchDescription(ListingSummary listing, IItemPageParser? parser, CancellationToken ct)
    {
        if (parser is null || string.IsNullOrWhiteSpace(listing.Url))
        {
            return null;
        }

        try
        {
            var html = await _client.GetPageHtml(listing.Url, ct);
            return parser.Parse(html)?.Description;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private string? Truncate(string? text) =>
        text is null || text.Length <= _options.MaxDescriptionChars
            ? text
            : text[.._options.MaxDescriptionChars];

    private static FamilySampleListing BuildSampleListing(ListingSummary listing, string? description) =>
        new(
            listing.ListingId,
            listing.Title,
            description,
            listing.Category,
            listing.Brand,
            listing.IsSold,
            listing.Price,
            listing.Url);
}
