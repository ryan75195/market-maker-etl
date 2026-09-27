using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Api;

public static class FamilyOnboardingEndpoints
{
    public static WebApplication MapFamilyOnboardingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/families/onboard", StartOnboarding);
        app.MapGet("/api/families/{familyId:int}/onboarding", GetOnboarding);
        app.MapPost("/api/families/{familyId:int}/onboarding/regenerate", Regenerate);
        app.MapPut("/api/families/{familyId:int}/onboarding/taxonomy", UpdateTaxonomy);
        app.MapPut("/api/families/{familyId:int}/onboarding/deal-settings", UpdateDealSettings);
        app.MapPost("/api/families/{familyId:int}/onboarding/approve", Approve);
        app.MapPost("/api/families/{familyId:int}/onboarding/reject", Reject);

        return app;
    }

    private static async Task<IResult> StartOnboarding(
        StartOnboardingRequest request,
        IFamilyOnboardingDraftingService drafting,
        IFamilyOnboardingPreviewService preview,
        CancellationToken ct)
    {
        var family = await drafting.StartOnboarding(request.Name, request.SearchTerm, request.Key, ct);
        if (family is null)
        {
            return Results.Conflict("A product family with that key already exists.");
        }

        await preview.RunPreview(family.Id, ct);
        var onboarding = await preview.GetOnboarding(family.Id, ct);
        return Results.Created($"/api/families/{family.Id}/onboarding", onboarding);
    }

    private static async Task<IResult> GetOnboarding(
        int familyId, IFamilyOnboardingPreviewService preview, CancellationToken ct)
    {
        var onboarding = await preview.GetOnboarding(familyId, ct);
        return onboarding is null ? Results.NotFound() : Results.Ok(onboarding);
    }

    private static async Task<IResult> Regenerate(
        int familyId,
        RegenerateOnboardingRequest request,
        IFamilyOnboardingDraftingService drafting,
        IFamilyOnboardingPreviewService preview,
        CancellationToken ct)
    {
        var family = await drafting.Regenerate(familyId, request.Feedback, ct);
        if (family is null)
        {
            return Results.NotFound();
        }

        await preview.RunPreview(familyId, ct);
        return Results.Ok(await preview.GetOnboarding(familyId, ct));
    }

    private static async Task<IResult> UpdateTaxonomy(
        int familyId,
        HttpRequest request,
        IProductFamilyStore families,
        IFamilyOnboardingPreviewService preview,
        CancellationToken ct)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null || family.State != FamilyState.Draft)
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(request.Body);
        var questionsJson = await reader.ReadToEndAsync(ct);

        var invalid = ValidateTaxonomy(questionsJson);
        if (invalid is not null)
        {
            return invalid;
        }

        await families.AddTaxonomyVersion(familyId, questionsJson, ct);
        await preview.RunPreview(familyId, ct);
        return Results.Ok(await preview.GetOnboarding(familyId, ct));
    }

    private static IResult? ValidateTaxonomy(string questionsJson)
    {
        TaxonomyDocument document;
        try
        {
            document = TaxonomyDocumentParser.Parse(questionsJson);
        }
        catch (TaxonomyParseException ex)
        {
            return TaxonomyValidationProblem([ex.Message]);
        }

        var validation = TaxonomyValidator.Validate(document);
        return validation.IsValid ? null : TaxonomyValidationProblem(validation.Errors);
    }

    private static IResult TaxonomyValidationProblem(IReadOnlyList<string> errors) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["Questions"] = errors.ToArray() });

    private static async Task<IResult> UpdateDealSettings(
        int familyId,
        UpdateOnboardingDealSettingsRequest request,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null || family.State != FamilyState.Draft)
        {
            return Results.NotFound();
        }

        var invalid = ValidateDealSettings(request);
        if (invalid is not null)
        {
            return invalid;
        }

        await families.UpdateFamily(
            familyId, null, null, request.DealGroupBy, request.DealMinDiscount, request.DealMinSold, ct);
        return Results.Ok(await families.GetFamily(familyId, ct));
    }

    private static IResult? ValidateDealSettings(UpdateOnboardingDealSettingsRequest request)
    {
        var errors = new List<string>();
        if (request.DealMinDiscount is decimal minDiscount && (minDiscount <= 0m || minDiscount > 1m))
        {
            errors.Add("DealMinDiscount must be greater than 0 and at most 1.");
        }

        if (request.DealMinSold is int minSold && minSold < 1)
        {
            errors.Add("DealMinSold must be at least 1.");
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["DealSettings"] = errors.ToArray() });
    }

    private static async Task<IResult> Approve(
        int familyId, IFamilyOnboardingLifecycleService lifecycle, CancellationToken ct)
    {
        var family = await lifecycle.Approve(familyId, ct);
        return family is null ? Results.NotFound() : Results.Ok(family);
    }

    private static async Task<IResult> Reject(
        int familyId, IFamilyOnboardingLifecycleService lifecycle, CancellationToken ct) =>
        await lifecycle.Reject(familyId, ct) ? Results.NoContent() : Results.NotFound();
}
