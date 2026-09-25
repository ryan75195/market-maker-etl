using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ListingRawDataStore : IListingRawDataStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public ListingRawDataStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<ListingRawData?> GetRawData(int listingEntityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var raw = await db.ListingRawData.FirstOrDefaultAsync(r => r.ListingEntityId == listingEntityId, ct);

        return raw is null
            ? null
            : new ListingRawData(
                GzipJson.Decompress(raw.SearchItemJsonGzip),
                GzipJson.Decompress(raw.ItemDetailJsonGzip));
    }
}
