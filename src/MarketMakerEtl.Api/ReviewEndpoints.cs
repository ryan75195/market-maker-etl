using System.Text.Json;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Api;

public static class ReviewEndpoints
{
    private const int DefaultReviewTake = 50;

    public static WebApplication MapReviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/families/{familyId:int}/review", GetReviewQueue);
        app.MapGet("/api/families/{familyId:int}/review/summary", GetReviewSummary);
        app.MapGet("/api/families/{familyId:int}/labels.jsonl", GetLabelsExport);

        return app;
    }

    private static async Task<IResult> GetReviewQueue(
        int familyId,
        IProductFamilyStore families,
        IClassificationReviewStore reviews,
        ClassificationReviewOptions options,
        CancellationToken ct,
        int take = DefaultReviewTake,
        string? question = null)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null)
        {
            return Results.NotFound();
        }

        if (family.LatestTaxonomyVersion is null)
        {
            return Results.Ok(new List<ClassificationReviewItem>());
        }

        var rows = await reviews.GetReviewQueue(
            familyId, family.LatestTaxonomyVersion.Id, question, options.ReviewThreshold, take, ct);
        var taxonomy = TaxonomyDocumentParser.Parse(family.LatestTaxonomyVersion.QuestionsJson);
        var items = BuildReviewItems(rows, taxonomy);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetReviewSummary(
        int familyId,
        IProductFamilyStore families,
        IClassificationReviewStore reviews,
        ClassificationReviewOptions options,
        CancellationToken ct)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null)
        {
            return Results.NotFound();
        }

        if (family.LatestTaxonomyVersion is null)
        {
            return Results.Ok(new ClassificationReviewSummary([], 0));
        }

        var counts = await reviews.GetReviewSummary(familyId, family.LatestTaxonomyVersion.Id, options.ReviewThreshold, ct);
        return Results.Ok(new ClassificationReviewSummary(counts, counts.Sum(c => c.Count)));
    }

    private static async Task<IResult> GetLabelsExport(
        int familyId,
        IProductFamilyStore families,
        IClassificationReviewStore reviews,
        ClassificationReviewOptions options,
        CancellationToken ct)
    {
        if (await families.GetFamily(familyId, ct) is null)
        {
            return Results.NotFound();
        }

        var listings = await reviews.GetLabelExportRows(familyId, ct);
        var body = string.Concat(listings.Select(listing => BuildExportLine(listing, options.ReviewThreshold) + "\n"));
        return Results.Text(body, "application/jsonl");
    }

    private static IReadOnlyList<ClassificationReviewItem> BuildReviewItems(
        IReadOnlyList<ClassificationReviewRow> rows, TaxonomyDocument taxonomy)
    {
        var items = new List<ClassificationReviewItem>(rows.Count);
        foreach (var row in rows)
        {
            var questionDef = taxonomy.Questions.FirstOrDefault(q => q.Key == row.Question);
            if (questionDef is not null)
            {
                items.Add(BuildReviewItem(row, questionDef));
            }
        }

        return items;
    }

    private static ClassificationReviewItem BuildReviewItem(ClassificationReviewRow row, TaxonomyQuestion questionDef)
    {
        var options = questionDef.Criteria
            .Select(criterion => new ClassificationReviewOption(criterion.Key, criterion.Value))
            .ToList();
        var probabilities = JsonSerializer.Deserialize<Dictionary<string, double>>(row.ProbabilitiesJson)
            ?? [];
        var sortedProbabilities = probabilities
            .OrderByDescending(probability => probability.Value)
            .Select(probability => new ClassificationProbability(probability.Key, probability.Value))
            .ToList();

        return new ClassificationReviewItem(
            row.ListingEntityId,
            row.Title,
            row.Url,
            row.Price,
            row.IsSold,
            row.Question,
            questionDef.Instructions,
            options,
            row.Choice,
            row.Confidence,
            row.Agreement,
            sortedProbabilities);
    }

    private static string BuildExportLine(ClassificationExportListing listing, double threshold)
    {
        var answers = listing.Answers
            .Where(answer => answer.IsApplicable
                && (answer.Source == ClassificationSource.Human || answer.Confidence >= threshold))
            .ToDictionary(answer => answer.Question, answer => answer.ResolvedChoice ?? answer.Choice, StringComparer.Ordinal);
        var state = ClassificationStateBuilder.BuildState(listing.Target);
        var export = new ClassificationLabelExport(listing.ListingId, listing.TaxonomyVersion, state, answers);

        return JsonSerializer.Serialize(export);
    }
}
