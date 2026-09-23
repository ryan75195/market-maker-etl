using MarketMakerEtl.Core.Data.Entities;

namespace MarketMakerEtl.Core.Data;

public static class ScrapeJobQueryExtensions
{
    public static IQueryable<ScrapeJobEntity> WhereEffectivelyEnabled(this IQueryable<ScrapeJobEntity> jobs) =>
        jobs.Where(j => j.IsEnabled
            && (!j.JobCategories.Any() || j.JobCategories.Any(jc => jc.Category!.IsEnabled)));
}
