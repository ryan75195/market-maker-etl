namespace MarketMakerEtl.Core.Data.Entities;

public sealed class SellerEntity
{
    public long SellerId { get; set; }

    public string? Name { get; set; }

    public int? NumSales { get; set; }

    public int? NumSellItems { get; set; }

    public int? RatingCount { get; set; }

    public double? RatingAverage { get; set; }

    public bool? IsProSeller { get; set; }

    public DateTime? AccountCreatedUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }
}
