using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingRefreshSchedulingService : IListingRefreshSchedulingService
{
    private readonly IListingRefreshService _refresh;
    private readonly ISchedulerStateStore _state;
    private readonly TimeProvider _timeProvider;
    private readonly ScheduleOptions _options;

    public ListingRefreshSchedulingService(
        IListingRefreshService refresh,
        ISchedulerStateStore state,
        TimeProvider timeProvider,
        ScheduleOptions options)
    {
        _refresh = refresh;
        _state = state;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task RefreshListingsIfDue(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lastRefresh = await _state.GetLastListingRefreshUtc(ct);

        if (lastRefresh is not null && lastRefresh.Value.AddHours(_options.RefreshIntervalHours) > now)
        {
            return;
        }

        await _refresh.RefreshActiveListings(ct);
        await _state.SetLastListingRefreshUtc(now, ct);
    }
}
