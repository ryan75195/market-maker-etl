using System.Globalization;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCoreServices(builder.Configuration);
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
    await factory.ApplyMigrations();
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")));

app.MapPost("/api/scrape/jobs", async (
    CreateScrapeJobRequest request,
    IScrapeStore store,
    CancellationToken ct) =>
{
    var jobId = await store.EnsureJob(request.SearchTerm, ct, request.Marketplace);
    var runId = await store.EnqueueRun(jobId, request.SearchTerm, TriggerType.Manual, ct);
    return Results.Accepted($"/api/scrape/runs/{runId}", new EnqueueRunResponse(runId));
});

app.MapGet("/api/scrape/runs/{runId:int}", async (int runId, IScrapeStore store, CancellationToken ct) =>
{
    var run = await store.GetRun(runId, ct);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapGet("/api/scrape/jobs/{jobId:int}/listings", async (
    int jobId,
    IScrapeStore store,
    CancellationToken ct) => Results.Ok(await store.GetListings(jobId, ct)));

app.MapGet("/api/jobs", async (IJobStore jobs, CancellationToken ct) =>
    Results.Ok(await jobs.GetJobs(ct)));

app.MapPost("/api/jobs", async (CreateJobRequest request, IJobStore jobs, ICategoryStore categories, CancellationToken ct) =>
{
    var invalid = await ValidateJobDetails(request.IntervalHours, request.CategoryIds, categories, ct);
    if (invalid is not null)
    {
        return invalid;
    }

    var job = await jobs.CreateJob(request.ToDetails(), ct);
    return Results.Created($"/api/jobs/{job.Id}", job);
});

app.MapGet("/api/jobs/{jobId:int}", async (int jobId, IJobStore jobs, CancellationToken ct) =>
{
    var job = await jobs.GetJob(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPut("/api/jobs/{jobId:int}", async (
    int jobId,
    UpdateJobRequest request,
    IJobStore jobs,
    ICategoryStore categories,
    CancellationToken ct) =>
{
    var invalid = await ValidateJobDetails(request.IntervalHours, request.CategoryIds, categories, ct);
    if (invalid is not null)
    {
        return invalid;
    }

    var job = await jobs.UpdateJob(jobId, request.ToDetails(), ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapDelete("/api/jobs/{jobId:int}", async (int jobId, IJobStore jobs, CancellationToken ct) =>
    await jobs.DeleteJob(jobId, ct) ? Results.NoContent() : Results.NotFound());

app.MapPost("/api/jobs/{jobId:int}/categories", async (
    int jobId,
    SetJobCategoriesRequest request,
    IJobStore jobs,
    ICategoryStore categories,
    CancellationToken ct) =>
{
    var missingCategoryIds = await FindMissingCategoryIds(request.CategoryIds, categories, ct);
    if (missingCategoryIds is not null)
    {
        return CategoryValidationProblem(missingCategoryIds);
    }

    var job = await jobs.SetJobCategories(jobId, request.CategoryIds, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPost("/api/jobs/{jobId:int}/enable", async (int jobId, IJobStore jobs, CancellationToken ct) =>
{
    var job = await jobs.SetJobEnabled(jobId, true, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPost("/api/jobs/{jobId:int}/disable", async (int jobId, IJobStore jobs, CancellationToken ct) =>
{
    var job = await jobs.SetJobEnabled(jobId, false, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPost("/api/jobs/{jobId:int}/run", async (
    int jobId,
    IJobStore jobs,
    IScrapeStore store,
    CancellationToken ct) =>
{
    var job = await jobs.GetJob(jobId, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    var runId = await store.EnqueueRun(jobId, job.SearchTerm, TriggerType.Manual, ct);
    await jobs.MarkQueued(jobId, DateTime.UtcNow, ct);
    return Results.Accepted($"/api/scrape/runs/{runId}", new EnqueueRunResponse(runId));
});

app.MapGet("/api/jobs/{jobId:int}/runs", async (
    int jobId,
    IJobStore jobs,
    IScrapeRunReportStore reports,
    CancellationToken ct) =>
{
    var job = await jobs.GetJob(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(await reports.GetRunsForJob(jobId, ct));
});

app.MapGet("/api/categories", async (ICategoryStore categories, CancellationToken ct) =>
    Results.Ok(await categories.GetCategories(ct)));

app.MapPost("/api/categories", async (CreateCategoryRequest request, ICategoryStore categories, CancellationToken ct) =>
{
    var category = await categories.CreateCategory(request.Name, request.IsEnabled, ct);
    return Results.Created($"/api/categories/{category.Id}", category);
});

app.MapGet("/api/categories/{categoryId:int}", async (int categoryId, ICategoryStore categories, CancellationToken ct) =>
{
    var category = await categories.GetCategory(categoryId, ct);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

app.MapPut("/api/categories/{categoryId:int}", async (
    int categoryId,
    UpdateCategoryRequest request,
    ICategoryStore categories,
    CancellationToken ct) =>
{
    var category = await categories.UpdateCategory(categoryId, request.Name, request.IsEnabled, ct);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

app.MapDelete("/api/categories/{categoryId:int}", async (int categoryId, ICategoryStore categories, CancellationToken ct) =>
    await categories.DeleteCategory(categoryId, ct) ? Results.NoContent() : Results.NotFound());

app.MapPost("/api/categories/{categoryId:int}/enable", async (int categoryId, ICategoryStore categories, CancellationToken ct) =>
{
    var category = await categories.SetCategoryEnabled(categoryId, true, ct);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

app.MapPost("/api/categories/{categoryId:int}/disable", async (int categoryId, ICategoryStore categories, CancellationToken ct) =>
{
    var category = await categories.SetCategoryEnabled(categoryId, false, ct);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

app.MapFamilyEndpoints();
app.MapClassificationEndpoints();
app.MapReviewEndpoints();

await app.RunAsync();

static async Task<IResult?> ValidateJobDetails(
    int intervalHours,
    IReadOnlyList<int>? categoryIds,
    ICategoryStore categories,
    CancellationToken ct)
{
    if (intervalHours < 1)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["IntervalHours"] = ["IntervalHours must be at least 1."]
        });
    }

    var missingCategoryIds = await FindMissingCategoryIds(categoryIds, categories, ct);
    return missingCategoryIds is null ? null : CategoryValidationProblem(missingCategoryIds);
}

static async Task<string[]?> FindMissingCategoryIds(
    IReadOnlyList<int>? categoryIds,
    ICategoryStore categories,
    CancellationToken ct)
{
    if (categoryIds is null || categoryIds.Count == 0)
    {
        return null;
    }

    var existingIds = (await categories.GetCategories(ct)).Select(c => c.Id).ToHashSet();
    var missing = categoryIds
        .Where(id => !existingIds.Contains(id))
        .Distinct()
        .Select(id => id.ToString(CultureInfo.InvariantCulture))
        .ToArray();
    return missing.Length == 0 ? null : missing;
}

static IResult CategoryValidationProblem(string[] missingCategoryIds) =>
    Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["CategoryIds"] = missingCategoryIds.Select(id => $"Category {id} does not exist.").ToArray()
    });

public partial class Program;

namespace MarketMakerEtl.Api
{
    public sealed record CreateScrapeJobRequest(string SearchTerm, Marketplace Marketplace = Marketplace.Ebay);

    public sealed record EnqueueRunResponse(int RunId);

    public sealed record HealthResponse(string Status);

    public sealed record CreateJobRequest(
        string SearchTerm,
        Marketplace Marketplace = Marketplace.Mercari,
        string? FilterInstructions = null,
        int IntervalHours = 24,
        bool IsEnabled = true,
        IReadOnlyList<int>? CategoryIds = null)
    {
        public JobDetails ToDetails() =>
            new(SearchTerm, Marketplace, FilterInstructions, IntervalHours, IsEnabled, CategoryIds ?? []);
    }

    public sealed record UpdateJobRequest(
        string SearchTerm,
        Marketplace Marketplace,
        string? FilterInstructions,
        int IntervalHours,
        bool IsEnabled,
        IReadOnlyList<int>? CategoryIds)
    {
        public JobDetails ToDetails() =>
            new(SearchTerm, Marketplace, FilterInstructions, IntervalHours, IsEnabled, CategoryIds ?? []);
    }

    public sealed record SetJobCategoriesRequest(IReadOnlyList<int> CategoryIds);

    public sealed record CreateCategoryRequest(string Name, bool IsEnabled = true);

    public sealed record UpdateCategoryRequest(string Name, bool IsEnabled);

    public sealed record SetJobFamilyRequest(int? ProductFamilyId);

    public sealed record CreateProductFamilyRequest(string Key, string Name, string ModelName);
}
