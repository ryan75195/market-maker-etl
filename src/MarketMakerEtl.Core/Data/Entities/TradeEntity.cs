using MarketMakerEtl.Core.Models.Trades;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class TradeEntity
{
    public int Id { get; set; }

    public int? DealSignalId { get; set; }

    public int? ListingEntityId { get; set; }

    public int? ProductFamilyId { get; set; }

    public string? PriceGroupKeyJson { get; set; }

    public DateTime BoughtUtc { get; set; }

    public decimal BuyPrice { get; set; }

    public decimal BuyShipping { get; set; }

    public decimal BuyFees { get; set; }

    public DateTime? SoldUtc { get; set; }

    public decimal? SellPrice { get; set; }

    public decimal? SellShipping { get; set; }

    public decimal? SellFees { get; set; }

    public TradeStatus Status { get; set; } = TradeStatus.Open;

    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
