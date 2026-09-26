using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Api;

public static class DealSignalEndpoints
{
    private const int DefaultTake = 50;

    public static WebApplication MapDealSignalEndpoints(this WebApplication app)
    {
        app.MapGet("/api/families/{familyId:int}/deals", GetDeals);

        return app;
    }

    private static async Task<IResult> GetDeals(
        int familyId,
        IProductFamilyStore families,
        IDealSignalStore signals,
        CancellationToken ct,
        DateTime? since = null,
        int take = DefaultTake)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null)
        {
            return Results.NotFound();
        }

        var deals = await signals.GetSignals(familyId, since, take, ct);
        return Results.Ok(deals);
    }
}
