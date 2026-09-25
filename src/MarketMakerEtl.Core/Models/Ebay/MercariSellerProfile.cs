namespace MarketMakerEtl.Core.Models.Ebay;

public sealed record MercariSellerProfile(
    long SellerId,
    string? Name = null,
    int? NumSales = null,
    int? NumSellItems = null,
    int? RatingCount = null,
    double? RatingAverage = null,
    bool? IsProSeller = null,
    DateTimeOffset? AccountCreatedUtc = null);
