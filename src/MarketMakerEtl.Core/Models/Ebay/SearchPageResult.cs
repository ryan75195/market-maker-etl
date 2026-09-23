namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record SearchPageResult(IReadOnlyList<ListingSummary> Listings, int? TotalCount);
