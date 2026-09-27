using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class DetailBacklogStore : IDetailBacklogStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public DetailBacklogStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetBacklogListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.GetGeneralListingsNeedingDetail(db, jobIds, limit, maxAttempts, ct);
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyBacklogListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.GetFamilyListingsNeedingDetail(db, jobIds, limit, maxAttempts, ct);
    }

    public async Task<int> CountFamilyListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.CountFamilyListingsNeedingDetail(db, jobIds, maxAttempts, ct);
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyInScopeListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.GetFamilyInScopeListingsNeedingDetail(db, jobIds, limit, maxAttempts, ct);
    }

    public async Task<IReadOnlyList<ListingDetailTarget>> GetFamilyUnclassifiedListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int limit, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.GetFamilyUnclassifiedListingsNeedingDetail(db, jobIds, limit, maxAttempts, ct);
    }

    public async Task<int> CountFamilyInScopeListingsNeedingDetail(
        IReadOnlyCollection<int> jobIds, int maxAttempts, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DetailBacklogQueries.CountFamilyInScopeListingsNeedingDetail(db, jobIds, maxAttempts, ct);
    }
}
