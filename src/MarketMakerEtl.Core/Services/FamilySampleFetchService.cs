using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilySampleFetchService : IFamilySampleFetchService
{
    private const int MaxDetailConcurrency = 5;

    private readonly ISearchPageService _search;
    private readonly IScrapeClient _client;
    private readonly MarketplaceAdapters _adapters;
    private readonly OnboardingOptions _options;

    public FamilySampleFetchService(
        ISearchPageService search, IScrapeClient client, MarketplaceAdapters adapters, OnboardingOptions options)
    {
        _search = search;
        _client = client;
        _adapters = adapters;
        _options = options;
    }

    public async Task<IReadOnlyList<FamilySampleListing>> FetchSample(string searchTerm, CancellationToken ct)
    {
        var knownSold = new HashSet<string>(StringComparer.Ordinal);
        var collected = await _search.Collect(searchTerm, Marketplace.Mercari, knownSold, ct);
        var candidates = collected.Listings.Take(_options.MaxSampleListings).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var parser = _adapters.ItemParsers.FirstOrDefault(p => p.Marketplace == Marketplace.Mercari);
        using var gate = new SemaphoreSlim(MaxDetailConcurrency);
        var results = await Task.WhenAll(candidates.Select(listing => FetchOne(listing, parser, gate, ct)));
        return results.ToList();
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
