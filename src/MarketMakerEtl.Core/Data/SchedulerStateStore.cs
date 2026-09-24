using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class SchedulerStateStore : ISchedulerStateStore
{
    private const int SingletonId = 1;

    private readonly IDbContextFactory<EtlDbContext> _factory;

    public SchedulerStateStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DateTime?> GetLastListingRefreshUtc(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var state = await db.SchedulerState.FindAsync([SingletonId], ct);
        return state?.LastListingRefreshUtc;
    }

    public async Task SetLastListingRefreshUtc(DateTime timestampUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var state = await db.SchedulerState.FindAsync([SingletonId], ct);
        if (state is null)
        {
            state = new SchedulerStateEntity { Id = SingletonId };
            db.SchedulerState.Add(state);
        }

        state.LastListingRefreshUtc = timestampUtc;
        await db.SaveChangesAsync(ct);
    }
}
