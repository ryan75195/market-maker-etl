using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Jobs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public static class JobViewFactory
{
    public static IQueryable<ScrapeJobEntity> IncludeCategories(this IQueryable<ScrapeJobEntity> jobs) =>
        jobs.Include(j => j.JobCategories).ThenInclude(jc => jc.Category);

    public static JobView ToView(this ScrapeJobEntity job) =>
        new(
            job.Id,
            job.SearchTerm,
            job.Marketplace,
            job.FilterInstructions,
            job.IntervalHours,
            job.IsEnabled,
            job.LastQueuedUtc,
            job.LastRunUtc,
            job.CreatedUtc,
            job.JobCategories
                .Where(jc => jc.Category is not null)
                .Select(jc => new CategoryView(jc.Category!.Id, jc.Category.Name, jc.Category.IsEnabled, jc.Category.CreatedUtc))
                .ToList(),
            job.ProductFamilyId);
}
