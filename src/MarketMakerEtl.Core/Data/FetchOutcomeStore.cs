using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class FetchOutcomeStore : IFetchOutcomeStore
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<EtlDbContext> _factory;
    private readonly TimeProvider _timeProvider;
    private readonly object _pruneGate = new();
    private DateTime? _lastPrunedBucketUtc;

    public FetchOutcomeStore(IDbContextFactory<EtlDbContext> factory, TimeProvider timeProvider)
    {
        _factory = factory;
        _timeProvider = timeProvider;
    }

    public async Task RecordOutcome(FetchOutcomeKind kind, CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var bucketStartUtc = RoundDownToMinute(nowUtc);
        var kindName = kind.ToString();

        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO FetchOutcomeBuckets (BucketStartUtc, Kind, Count)
            VALUES ({bucketStartUtc}, {kindName}, 1)
            ON CONFLICT(BucketStartUtc, Kind) DO UPDATE SET Count = Count + 1
            """,
            ct);

        if (ShouldPruneFor(bucketStartUtc))
        {
            await PruneStaleBuckets(db, nowUtc, ct);
        }
    }

    private bool ShouldPruneFor(DateTime bucketStartUtc)
    {
        lock (_pruneGate)
        {
            if (_lastPrunedBucketUtc == bucketStartUtc)
            {
                return false;
            }

            _lastPrunedBucketUtc = bucketStartUtc;
            return true;
        }
    }

    public async Task<FetchOutcomeSnapshot> GetRecentOutcomes(TimeSpan window, CancellationToken ct)
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime - window;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var buckets = await db.FetchOutcomeBuckets
            .Where(bucket => bucket.BucketStartUtc >= cutoffUtc)
            .ToListAsync(ct);

        return new FetchOutcomeSnapshot(
            SumFor(buckets, FetchOutcomeKind.Success),
            SumFor(buckets, FetchOutcomeKind.Infrastructure),
            SumFor(buckets, FetchOutcomeKind.NotFound),
            SumFor(buckets, FetchOutcomeKind.Other));
    }

    private static int SumFor(IReadOnlyList<FetchOutcomeBucketEntity> buckets, FetchOutcomeKind kind)
    {
        var kindName = kind.ToString();
        return buckets.Where(bucket => bucket.Kind == kindName).Sum(bucket => bucket.Count);
    }

    private static Task PruneStaleBuckets(EtlDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoffUtc = nowUtc - MaxRetention;
        return db.FetchOutcomeBuckets
            .Where(bucket => bucket.BucketStartUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }

    private static DateTime RoundDownToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
}
