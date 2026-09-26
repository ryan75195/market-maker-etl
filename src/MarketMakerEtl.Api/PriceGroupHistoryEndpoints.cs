using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Api;

public static class PriceGroupHistoryEndpoints
{
    private const int DefaultHistoryWeeks = 12;

    public static WebApplication MapPriceGroupHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/families/{familyId:int}/price-groups/history", GetPriceGroupHistory);

        return app;
    }

    private static async Task<IResult> GetPriceGroupHistory(
        int familyId,
        string[]? where,
        IProductFamilyStore families,
        IPriceGroupQueryService priceGroups,
        CancellationToken ct,
        string bucket = "week",
        int weeks = DefaultHistoryWeeks,
        string basis = "listed",
        bool includeEstimatedDates = false,
        string? trim = null)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family?.LatestTaxonomyVersion is null)
        {
            return Results.NotFound();
        }

        var questions = PriceGroupEndpoints.LoadQuestions(family.LatestTaxonomyVersion.QuestionsJson);
        var errors = new List<string>();
        var whereMap = PriceGroupEndpoints.ParseWhere(where ?? [], questions, errors);
        ValidateHistoryParameters(bucket, weeks, basis, trim, errors, out var parsedBucket, out var parsedBasis, out var trimIqr);

        if (errors.Count > 0)
        {
            return PriceGroupEndpoints.QuestionValidationProblem(errors);
        }

        var query = new PriceGroupHistoryQuery(
            family.LatestTaxonomyVersion.Id, whereMap, parsedBucket, weeks, parsedBasis, includeEstimatedDates, trimIqr);
        return Results.Ok(await priceGroups.GetPriceGroupHistory(query, ct));
    }

    private static void ValidateHistoryParameters(
        string bucket,
        int weeks,
        string basis,
        string? trim,
        List<string> errors,
        out PriceGroupHistoryBucketGranularity parsedBucket,
        out PriceGroupHistoryBasis parsedBasis,
        out bool trimIqr)
    {
        if (!TryParseBucket(bucket, out parsedBucket))
        {
            errors.Add("bucket must be 'week' or 'day'.");
        }

        if (!TryParseBasis(basis, out parsedBasis))
        {
            errors.Add("basis must be 'listed' or 'net'.");
        }

        if (weeks <= 0)
        {
            errors.Add("weeks must be positive.");
        }

        if (!PriceGroupEndpoints.TryParseTrim(trim, out trimIqr))
        {
            errors.Add("trim must be 'iqr'.");
        }
    }

    private static bool TryParseBucket(string bucket, out PriceGroupHistoryBucketGranularity parsed)
    {
        switch (bucket)
        {
            case "week":
                parsed = PriceGroupHistoryBucketGranularity.Week;
                return true;
            case "day":
                parsed = PriceGroupHistoryBucketGranularity.Day;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static bool TryParseBasis(string basis, out PriceGroupHistoryBasis parsed)
    {
        switch (basis)
        {
            case "listed":
                parsed = PriceGroupHistoryBasis.Listed;
                return true;
            case "net":
                parsed = PriceGroupHistoryBasis.Net;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
