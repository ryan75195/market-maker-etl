using MarketMakerEtl.Api;
using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCoreServices();
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
    var jobId = await store.EnsureJob(request.SearchTerm, ct);
    var runId = await store.EnqueueRun(jobId, request.SearchTerm, ct);
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

await app.RunAsync();

namespace MarketMakerEtl.Api
{
    public sealed record CreateScrapeJobRequest(string SearchTerm);

    public sealed record EnqueueRunResponse(int RunId);

    public sealed record HealthResponse(string Status);
}
