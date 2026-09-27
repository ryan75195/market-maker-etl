using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class DetailBacklogThrottleService : IDetailBacklogThrottleService
{
    private readonly DetailBacklogOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DetailBacklogThrottleService> _logger;
    private readonly object _budgetLock = new();
    private readonly List<DateTime> _recentFetchTimestampsUtc = [];
    private readonly object _backoffLock = new();
    private int _consecutiveInfrastructureFailureTicks;
    private DateTime? _backoffUntilUtc;

    public DetailBacklogThrottleService(
        DetailBacklogOptions options, TimeProvider timeProvider, ILogger<DetailBacklogThrottleService> logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsBackingOffInfrastructureFailures()
    {
        lock (_backoffLock)
        {
            return _backoffUntilUtc is { } backoffUntilUtc && _timeProvider.GetUtcNow().UtcDateTime < backoffUntilUtc;
        }
    }

    public int RemainingHourlyBudget()
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);

        lock (_budgetLock)
        {
            _recentFetchTimestampsUtc.RemoveAll(timestamp => timestamp <= cutoffUtc);
            return Math.Max(0, _options.MaxFetchesPerHour - _recentFetchTimestampsUtc.Count);
        }
    }

    public void RecordFetchAttempt()
    {
        lock (_budgetLock)
        {
            _recentFetchTimestampsUtc.Add(_timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    public void ObserveTickResult(DetailBacklogTickResult result)
    {
        if (result.Attempted == 0)
        {
            return;
        }

        if (result.Succeeded > 0)
        {
            ResetBackoff();
            return;
        }

        if (IsInfrastructureOnlyFailure(result))
        {
            EngageBackoff(result.Failures.Count);
        }
    }

    private static bool IsInfrastructureOnlyFailure(DetailBacklogTickResult result) =>
        result.Failures.Count > 0
        && result.Failures.All(
            failure => failure.IssueType == ItemDetailFetchService.InfrastructureUnavailableIssueType);

    private void ResetBackoff()
    {
        int previousStreak;

        lock (_backoffLock)
        {
            previousStreak = _consecutiveInfrastructureFailureTicks;
            _consecutiveInfrastructureFailureTicks = 0;
            _backoffUntilUtc = null;
        }

        if (previousStreak > 0)
        {
            _logger.LogWarning(
                "Detail backlog recovered after {ConsecutiveFailureTicks} consecutive infrastructure-only " +
                "failure tick(s); resuming normal ticking.",
                previousStreak);
        }
    }

    private void EngageBackoff(int failureCount)
    {
        DateTime backoffUntilUtc;

        lock (_backoffLock)
        {
            _consecutiveInfrastructureFailureTicks++;
            var delaySeconds = ComputeBackoffSeconds(_consecutiveInfrastructureFailureTicks);
            backoffUntilUtc = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(delaySeconds);
            _backoffUntilUtc = backoffUntilUtc;
        }

        _logger.LogWarning(
            "Detail backlog tick saw only infrastructure-unavailable failures ({FailureCount}); backing off until {BackoffUntilUtc:o}.",
            failureCount,
            backoffUntilUtc);
    }

    private int ComputeBackoffSeconds(int consecutiveFailureTicks)
    {
        var uncappedSeconds = _options.InfrastructureBackoffBaseSeconds * Math.Pow(2, consecutiveFailureTicks - 1);
        return (int)Math.Min(uncappedSeconds, _options.InfrastructureBackoffMaxSeconds);
    }
}
