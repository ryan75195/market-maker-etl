using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

internal sealed class ConcurrencyTrackingScrapeClient : IScrapeClient
{
    private readonly TimeSpan _delay;
    private int _inFlight;
    private int _peakInFlight;
    private int _completedCalls;

    public ConcurrencyTrackingScrapeClient(TimeSpan delay)
    {
        _delay = delay;
    }

    public int PeakInFlight => _peakInFlight;

    public int CompletedCalls => _completedCalls;

    public async Task<string> GetPageHtml(string url, CancellationToken ct)
    {
        var current = Interlocked.Increment(ref _inFlight);
        TrackPeak(current);

        try
        {
            await Task.Delay(_delay, ct);
            return "<html/>";
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            Interlocked.Increment(ref _completedCalls);
        }
    }

    private void TrackPeak(int current)
    {
        int initial;
        do
        {
            initial = _peakInFlight;
            if (current <= initial)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _peakInFlight, current, initial) != initial);
    }
}
