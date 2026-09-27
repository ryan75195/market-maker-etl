using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class ClassificationThrottleService : IClassificationThrottleService
{
    private const int ProbeBudget = 25;

    private readonly ClassifierOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClassificationThrottleService> _logger;
    private readonly object _backoffLock = new();
    private int _consecutiveFailureTicks;
    private DateTime? _backoffUntilUtc;

    public ClassificationThrottleService(
        ClassifierOptions options, TimeProvider timeProvider, ILogger<ClassificationThrottleService> logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsBackingOffFailures()
    {
        lock (_backoffLock)
        {
            return _backoffUntilUtc is { } backoffUntilUtc && _timeProvider.GetUtcNow().UtcDateTime < backoffUntilUtc;
        }
    }

    public bool IsProbingAfterFailures()
    {
        lock (_backoffLock)
        {
            return _consecutiveFailureTicks > 0;
        }
    }

    public int ResolveTickBudget()
    {
        lock (_backoffLock)
        {
            return _consecutiveFailureTicks > 0
                ? Math.Min(ProbeBudget, _options.MaxListingsPerTick)
                : _options.MaxListingsPerTick;
        }
    }

    public void ObserveTickResult(ClassificationTickResult result)
    {
        if (result.ListingsSelected == 0)
        {
            return;
        }

        if (result.ListingsClassified > 0)
        {
            ResetBackoff();
            return;
        }

        EngageBackoff(result.Failures.Count);
    }

    private void ResetBackoff()
    {
        int previousStreak;

        lock (_backoffLock)
        {
            previousStreak = _consecutiveFailureTicks;
            _consecutiveFailureTicks = 0;
            _backoffUntilUtc = null;
        }

        if (previousStreak > 0)
        {
            _logger.LogWarning(
                "Classification recovered after {ConsecutiveFailureTicks} consecutive fully-failed tick(s); resuming normal ticking.",
                previousStreak);
        }
    }

    private void EngageBackoff(int failureCount)
    {
        DateTime backoffUntilUtc;

        lock (_backoffLock)
        {
            _consecutiveFailureTicks++;
            var delayMinutes = ComputeBackoffMinutes(_consecutiveFailureTicks);
            backoffUntilUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(delayMinutes);
            _backoffUntilUtc = backoffUntilUtc;
        }

        _logger.LogWarning(
            "Classification tick saw only failed batches ({FailureCount}); backing off until {BackoffUntilUtc:o}.",
            failureCount,
            backoffUntilUtc);
    }

    private double ComputeBackoffMinutes(int consecutiveFailureTicks)
    {
        var uncappedMinutes = _options.FailureBackoffBaseMinutes * Math.Pow(2, consecutiveFailureTicks - 1);
        return Math.Min(uncappedMinutes, _options.FailureBackoffMaxMinutes);
    }
}
