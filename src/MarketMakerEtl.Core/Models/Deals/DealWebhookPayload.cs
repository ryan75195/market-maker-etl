namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealWebhookPayload(
    string? Title,
    string? Url,
    decimal LandedPrice,
    IReadOnlyDictionary<string, string> GroupKey,
    decimal SoldNetMedian,
    decimal Discount);
