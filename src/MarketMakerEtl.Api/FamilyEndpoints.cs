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

    private static IResult TaxonomyValidationProblem(IReadOnlyList<string> errors) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Questions"] = errors.ToArray()
        });
}
