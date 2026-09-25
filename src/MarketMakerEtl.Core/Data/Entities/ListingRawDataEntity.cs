using System.Diagnostics.CodeAnalysis;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class ListingRawDataEntity
{
    public int Id { get; set; }

    public int ListingEntityId { get; set; }

    [SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "EF Core BLOB column")]
    public byte[]? SearchItemJsonGzip { get; set; }

    [SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "EF Core BLOB column")]
    public byte[]? ItemDetailJsonGzip { get; set; }

    public DateTime? SearchItemJsonUpdatedUtc { get; set; }

    public DateTime? ItemDetailJsonUpdatedUtc { get; set; }
}
