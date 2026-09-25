using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Api;

public static class ClassificationEndpoints
{
    public static WebApplication MapClassificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/listings/{listingId:int}/classification", GetClassification);
        app.MapPut("/api/listings/{listingId:int}/classification/{question}", SetHumanAnswer);
        app.MapPost("/api/listings/{listingId:int}/classification/{question}/confirm", ConfirmAnswer);

        return app;
    }

    private static async Task<IResult> GetClassification(
        int listingId, IListingClassificationStore classifications, CancellationToken ct)
    {
        var classification = await classifications.GetClassification(listingId, ct);
        return classification is null ? Results.NotFound() : Results.Ok(classification);
    }

    private static async Task<IResult> SetHumanAnswer(
        int listingId,
        string question,
        SetClassificationChoiceRequest request,
        IProductFamilyStore families,
        IClassificationReviewStore reviews,
        CancellationToken ct)
    {
        var taxonomy = await LoadTaxonomy(listingId, reviews, families, ct);
        if (taxonomy is null)
        {
            return Results.NotFound();
        }

        var questionDef = taxonomy.Questions.FirstOrDefault(q => q.Key == question);
        if (questionDef is null)
        {
            return UnknownQuestionProblem(question);
        }

        if (!questionDef.Criteria.ContainsKey(request.Choice))
        {
            return UnknownOptionProblem(request.Choice);
        }

        var updated = await reviews.SetHumanAnswer(listingId, question, request.Choice, taxonomy, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<IResult> ConfirmAnswer(
        int listingId,
        string question,
        IListingClassificationStore classifications,
        IProductFamilyStore families,
        IClassificationReviewStore reviews,
        CancellationToken ct)
    {
        var taxonomy = await LoadTaxonomy(listingId, reviews, families, ct);
        if (taxonomy is null)
        {
            return Results.NotFound();
        }

        var classification = await classifications.GetClassification(listingId, ct);
        var answer = classification?.Answers.FirstOrDefault(a => a.Question == question);
        if (answer is null)
        {
            return Results.NotFound();
        }

        var updated = await reviews.SetHumanAnswer(listingId, question, answer.Choice, taxonomy, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<TaxonomyDocument?> LoadTaxonomy(
        int listingId, IClassificationReviewStore reviews, IProductFamilyStore families, CancellationToken ct)
    {
        var versionId = await reviews.GetTaxonomyVersionId(listingId, ct);
        if (versionId is null)
        {
            return null;
        }

        var taxonomyVersion = await families.GetTaxonomyVersionById(versionId.Value, ct);
        return taxonomyVersion is null ? null : TaxonomyDocumentParser.Parse(taxonomyVersion.QuestionsJson);
    }

    private static IResult UnknownQuestionProblem(string question) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Question"] = [$"Unknown question '{question}'."]
        });

    private static IResult UnknownOptionProblem(string choice) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Choice"] = [$"Unknown option '{choice}'."]
        });
}

public sealed record SetClassificationChoiceRequest(string Choice);
