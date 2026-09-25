using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Api;

public static class ClassificationEndpoints
{
    public static WebApplication MapClassificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/listings/{listingId:int}/classification", GetClassification);

        return app;
    }

    private static async Task<IResult> GetClassification(
        int listingId, IListingClassificationStore classifications, CancellationToken ct)
    {
        var classification = await classifications.GetClassification(listingId, ct);
        return classification is null ? Results.NotFound() : Results.Ok(classification);
    }
}
