using System.Text.RegularExpressions;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Api;

public static class FamilyEndpoints
{
    public static WebApplication MapFamilyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/families", async (IProductFamilyStore families, CancellationToken ct) =>
            Results.Ok(await families.GetFamilies(ct)));

        app.MapPost("/api/families", CreateFamily);

        app.MapGet("/api/families/{familyId:int}", GetFamily);

        app.MapPut("/api/families/{familyId:int}", UpdateFamily);

        app.MapPost("/api/families/{familyId:int}/taxonomies", AddTaxonomyVersion);

        app.MapGet("/api/families/{familyId:int}/taxonomies/{version:int}", GetTaxonomyVersion);

        app.MapPut("/api/jobs/{jobId:int}/family", SetJobFamily);

        return app;
    }

    private static async Task<IResult> GetFamily(int familyId, IProductFamilyStore families, CancellationToken ct)
    {
        var family = await families.GetFamily(familyId, ct);
        return family is null ? Results.NotFound() : Results.Ok(family);
    }

    private static async Task<IResult> GetTaxonomyVersion(
        int familyId,
        int version,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        var taxonomyVersion = await families.GetTaxonomyVersion(familyId, version, ct);
        return taxonomyVersion is null ? Results.NotFound() : Results.Ok(taxonomyVersion);
    }

    private static async Task<IResult> SetJobFamily(
        int jobId,
        SetJobFamilyRequest request,
        IJobStore jobs,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        if (request.ProductFamilyId is int familyId && await families.GetFamily(familyId, ct) is null)
        {
            return Results.NotFound();
        }

        var updated = await families.SetJobFamily(jobId, request.ProductFamilyId, ct);
        return updated ? Results.Ok(await jobs.GetJob(jobId, ct)) : Results.NotFound();
    }

    private static async Task<IResult> CreateFamily(
        CreateProductFamilyRequest request,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        var invalid = ValidateFamilyKey(request.Key);
        if (invalid is not null)
        {
            return invalid;
        }

        var family = await families.CreateFamily(request.Key, request.Name, request.ModelName, ct);
        return family is null
            ? Results.Conflict($"A product family with key '{request.Key}' already exists.")
            : Results.Created($"/api/families/{family.Id}", family);
    }

    private static async Task<IResult> UpdateFamily(
        int familyId,
        UpdateProductFamilyRequest request,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        var invalid = ValidateUpdateRequest(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var updated = await families.UpdateFamily(
            familyId,
            request.Name,
            request.ModelName,
            request.DealGroupBy,
            request.DealMinDiscount,
            request.DealMinSold,
            ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static IResult? ValidateUpdateRequest(UpdateProductFamilyRequest request)
    {
        if (request.ModelName is not null)
        {
            var invalid = ValidateModelName(request.ModelName);
            if (invalid is not null)
            {
                return invalid;
            }
        }

        return ValidateDealSettings(request.DealGroupBy, request.DealMinDiscount, request.DealMinSold);
    }

    private static IResult? ValidateDealSettings(string? dealGroupBy, decimal? dealMinDiscount, int? dealMinSold)
    {
        var errors = new List<string>();

        if (dealGroupBy is { Length: > 0 } && !IsValidDealGroupBy(dealGroupBy))
        {
            errors.Add("DealGroupBy must be a comma-separated list of question keys.");
        }

        if (dealMinDiscount is decimal minDiscount && (minDiscount <= 0m || minDiscount > 1m))
        {
            errors.Add("DealMinDiscount must be greater than 0 and at most 1.");
        }

        if (dealMinSold is int minSold && minSold < 1)
        {
            errors.Add("DealMinSold must be at least 1.");
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["DealSettings"] = errors.ToArray() });
    }

    private static bool IsValidDealGroupBy(string dealGroupBy)
    {
        var questions = dealGroupBy.Split(',', StringSplitOptions.TrimEntries);
        return questions.Length > 0 && questions.All(q => q.Length > 0) && questions.Distinct().Count() == questions.Length;
    }

    private static async Task<IResult> AddTaxonomyVersion(
        int familyId,
        HttpRequest request,
        IProductFamilyStore families,
        CancellationToken ct)
    {
        if (await families.GetFamily(familyId, ct) is null)
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(request.Body);
        var questionsJson = await reader.ReadToEndAsync(ct);

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
        if (!validation.IsValid)
        {
            return TaxonomyValidationProblem(validation.Errors);
        }

        var version = await families.AddTaxonomyVersion(familyId, questionsJson, ct);
        return version is null
            ? Results.NotFound()
            : Results.Created($"/api/families/{familyId}/taxonomies/{version.Version}", version);
    }

    private static IResult? ValidateFamilyKey(string key)
    {
        var isKebabCase = Regex.IsMatch(key, "^[a-z0-9]+(-[a-z0-9]+)*$");
        return isKebabCase
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Key"] = ["Key must be kebab-case."]
            });
    }

    private static IResult? ValidateModelName(string modelName)
    {
        var isSafeDirectoryName = Regex.IsMatch(modelName, "^[a-z0-9-]+$");
        return isSafeDirectoryName
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ModelName"] = ["ModelName must match [a-z0-9-]+."]
            });
    }

    private static IResult TaxonomyValidationProblem(IReadOnlyList<string> errors) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Questions"] = errors.ToArray()
        });
}
