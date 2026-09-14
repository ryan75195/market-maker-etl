using System.Globalization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariSearchUrlService : IEbaySearchUrlService
{
    private readonly string _searchBase = "https://www.mercari.com/us/search";

    public Marketplace Marketplace => Marketplace.Mercari;

    public bool SupportsPagination => false;

    public string BuildSearch(string searchTerm, bool sold, int page) =>
        BuildSearch(new MercariSearchRequest(searchTerm, sold));

    public string BuildSearch(MercariSearchRequest request)
    {
        var query = new List<string>
        {
            $"keyword={Uri.EscapeDataString(request.SearchTerm)}"
        };

        AddIfPresent(query, "itemStatuses", request.Sold ? "2" : null);
        AddIfPresent(query, "brandIds", request.BrandId);
        AddIfPresent(query, "categoryIds", request.CategoryId);
        AddIfPresent(query, "itemConditions", request.Condition);
        AddIfPresent(query, "minPrice", FormatPrice(request.MinPrice));
        AddIfPresent(query, "maxPrice", FormatPrice(request.MaxPrice));

        return $"{_searchBase}?{string.Join('&', query)}";
    }

    private static void AddIfPresent(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{key}={value}");
        }
    }

    private static string? FormatPrice(decimal? price) =>
        price?.ToString(CultureInfo.InvariantCulture);
}
