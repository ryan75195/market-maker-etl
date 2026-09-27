using System.Collections.Concurrent;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class FetchOutcomeMonitor : IFetchOutcomeMonitor
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromHours(24);

    private readonly ConcurrentQueue<FetchOutcomeEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public FetchOutcomeMonitor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Record(FetchOutcomeKind kind)
    {
        _entries.Enqueue(new FetchOutcomeEntry(_timeProvider.GetUtcNow().UtcDateTime, kind));
        PruneStaleEntries();
    }

    public FetchOutcomeSnapshot GetRecentOutcomes(TimeSpan window)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - window;
        var recent = _entries.Where(entry => entry.TimestampUtc >= cutoff).ToList();

        return new FetchOutcomeSnapshot(
            recent.Count(entry => entry.Kind == FetchOutcomeKind.Success),
            recent.Count(entry => entry.Kind == FetchOutcomeKind.Infrastructure),
            recent.Count(entry => entry.Kind == FetchOutcomeKind.NotFound),
            recent.Count(entry => entry.Kind == FetchOutcomeKind.Other));
    }

    private void PruneStaleEntries()
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - MaxRetention;
        while (_entries.TryPeek(out var oldest) && oldest.TimestampUtc < cutoff)
        {
            _entries.TryDequeue(out _);
        }
    }

    private sealed record FetchOutcomeEntry(DateTime TimestampUtc, FetchOutcomeKind Kind);
}
