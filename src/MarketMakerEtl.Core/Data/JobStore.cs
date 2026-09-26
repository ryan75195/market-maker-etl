using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class JobStore : IJobStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public JobStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<JobView> CreateJob(JobDetails details, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = new ScrapeJobEntity
        {
            SearchTerm = details.SearchTerm,
            Marketplace = details.Marketplace,
            FilterInstructions = details.FilterInstructions,
            IntervalHours = details.IntervalHours,
            IsEnabled = details.IsEnabled,
            CreatedUtc = DateTime.UtcNow
        };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await ApplyCategories(db, job.Id, details.CategoryIds, ct);
        return (await LoadView(db, job.Id, ct))!;
    }

    public async Task<IReadOnlyList<JobView>> GetJobs(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var jobs = await db.ScrapeJobs.IncludeCategories().OrderBy(j => j.Id).ToListAsync(ct);
        return jobs.Select(JobViewFactory.ToView).ToList();
    }

    public async Task<JobView?> GetJob(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadView(db, jobId, ct);
    }

    public async Task<JobView?> UpdateJob(int jobId, JobDetails details, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return null;
        }

        job.SearchTerm = details.SearchTerm;
        job.Marketplace = details.Marketplace;
        job.FilterInstructions = details.FilterInstructions;
        job.IntervalHours = details.IntervalHours;
        job.IsEnabled = details.IsEnabled;
        await db.SaveChangesAsync(ct);
        await ApplyCategories(db, jobId, details.CategoryIds, ct);
        return await LoadView(db, jobId, ct);
    }

    public async Task<bool> DeleteJob(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return false;
        }

        db.ScrapeJobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<JobView?> SetJobEnabled(int jobId, bool isEnabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return null;
        }

        job.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
        return await LoadView(db, jobId, ct);
    }

    public async Task<JobView?> SetJobCategories(int jobId, IReadOnlyList<int> categoryIds, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return null;
        }

        await ApplyCategories(db, jobId, categoryIds, ct);
        return await LoadView(db, jobId, ct);
    }

    public async Task<JobView?> MarkQueued(int jobId, DateTime queuedUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FindAsync([jobId], ct);
        if (job is null)
        {
            return null;
        }

        job.LastQueuedUtc = queuedUtc;
        await db.SaveChangesAsync(ct);
        return await LoadView(db, jobId, ct);
    }

    public async Task<IReadOnlyList<JobView>> GetEffectivelyEnabledJobs(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var jobs = await db.ScrapeJobs.IncludeCategories()
            .WhereEffectivelyEnabled()
            .OrderBy(j => j.Id)
            .ToListAsync(ct);
        return jobs.Select(JobViewFactory.ToView).ToList();
    }

    public async Task<bool> HasQueuedOrRunningRun(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ScrapeRuns.AnyAsync(
            r => r.JobId == jobId
                && (r.Status == nameof(ScrapeRunStatus.Queued) || r.Status == nameof(ScrapeRunStatus.Running)),
            ct);
    }

    private static async Task ApplyCategories(
        EtlDbContext db,
        int jobId,
        IReadOnlyList<int> categoryIds,
        CancellationToken ct)
    {
        var existing = await db.JobCategories.Where(jc => jc.ScrapeJobId == jobId).ToListAsync(ct);
        db.JobCategories.RemoveRange(existing);
        foreach (var categoryId in categoryIds.Distinct())
        {
            db.JobCategories.Add(new JobCategoryEntity { ScrapeJobId = jobId, CategoryId = categoryId });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<JobView?> LoadView(EtlDbContext db, int jobId, CancellationToken ct)
    {
        var job = await db.ScrapeJobs.IncludeCategories().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job?.ToView();
    }
}
