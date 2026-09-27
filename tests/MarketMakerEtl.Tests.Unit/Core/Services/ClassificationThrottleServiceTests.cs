using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ClassificationThrottleServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Should_not_be_backing_off_before_any_tick_has_been_observed()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime));

        Assert.That(throttle.IsBackingOffFailures(), Is.False);
    }

    [Test]
    public void Should_resolve_the_full_tick_budget_before_any_failure_has_been_observed()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime), maxListingsPerTick: 2000);

        Assert.Multiple(() =>
        {
            Assert.That(throttle.IsProbingAfterFailures(), Is.False);
            Assert.That(throttle.ResolveTickBudget(), Is.EqualTo(2000));
        });
    }

    [Test]
    public void Should_leave_backoff_disengaged_when_the_tick_made_no_classification_attempts()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime));

        throttle.ObserveTickResult(new ClassificationTickResult(0, 0, 0, []));

        Assert.That(throttle.IsBackingOffFailures(), Is.False);
    }

    [Test]
    public void Should_engage_backoff_and_start_probing_after_a_tick_where_every_batch_failed()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseMinutes: 5, backoffMaxMinutes: 240, maxListingsPerTick: 2000);

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));

        Assert.Multiple(() =>
        {
            Assert.That(throttle.IsBackingOffFailures(), Is.True);
            Assert.That(throttle.IsProbingAfterFailures(), Is.True);
            Assert.That(throttle.ResolveTickBudget(), Is.EqualTo(25));
        });
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.That(throttle.IsBackingOffFailures(), Is.False);
    }

    [Test]
    public void Should_skip_the_next_tick_while_backing_off()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseMinutes: 5, backoffMaxMinutes: 240);

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));

        timeProvider.Advance(TimeSpan.FromMinutes(4));
        Assert.That(throttle.IsBackingOffFailures(), Is.True, "a tick 4 minutes later should still be skipped");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.That(throttle.IsBackingOffFailures(), Is.False, "a tick 5 minutes later should no longer be skipped");
    }

    [Test]
    public void Should_double_the_backoff_delay_for_each_further_consecutive_fully_failed_tick()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseMinutes: 5, backoffMaxMinutes: 240);

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.That(throttle.IsBackingOffFailures(), Is.False);

        throttle.ObserveTickResult(new ClassificationTickResult(1, 5, 0, [Failure()]));
        timeProvider.Advance(TimeSpan.FromMinutes(9));
        Assert.That(
            throttle.IsBackingOffFailures(),
            Is.True,
            "the second consecutive fully-failed tick should back off for 10 minutes, not the base 5");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.That(throttle.IsBackingOffFailures(), Is.False);
    }

    [Test]
    public void Should_cap_the_backoff_delay_at_the_configured_maximum()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseMinutes: 100, backoffMaxMinutes: 150);
        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));
        timeProvider.Advance(TimeSpan.FromMinutes(100));

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));
        timeProvider.Advance(TimeSpan.FromMinutes(149));
        Assert.That(
            throttle.IsBackingOffFailures(),
            Is.True,
            "the uncapped delay would be 200 minutes, but it must be capped at 150");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.That(throttle.IsBackingOffFailures(), Is.False);
    }

    [Test]
    public void Should_reset_the_backoff_streak_and_budget_after_a_tick_with_a_success()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseMinutes: 5, backoffMaxMinutes: 240, maxListingsPerTick: 2000);
        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 10, []));

        Assert.Multiple(() =>
        {
            Assert.That(throttle.IsBackingOffFailures(), Is.False);
            Assert.That(throttle.IsProbingAfterFailures(), Is.False);
            Assert.That(throttle.ResolveTickBudget(), Is.EqualTo(2000));
        });

        throttle.ObserveTickResult(new ClassificationTickResult(1, 10, 0, [Failure()]));

        Assert.That(throttle.IsBackingOffFailures(), Is.True);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.That(
            throttle.IsBackingOffFailures(),
            Is.False,
            "the streak should restart at the base delay after a reset, not continue escalating");
    }

    private static ClassificationBatchFailure Failure() => new(10, 5, "OpenAI returned 401 Unauthorized");

    private static ClassificationThrottleService CreateThrottle(
        TimeProvider timeProvider,
        int maxListingsPerTick = 2000,
        int backoffBaseMinutes = 5,
        int backoffMaxMinutes = 240)
    {
        var options = new ClassifierOptions(
            TickMinutes: 5,
            MaxListingsPerTick: maxListingsPerTick,
            DegradedAfterFailedBatches: 5,
            FailureBackoffBaseMinutes: backoffBaseMinutes,
            FailureBackoffMaxMinutes: backoffMaxMinutes);

        return new ClassificationThrottleService(options, timeProvider, NullLogger<ClassificationThrottleService>.Instance);
    }
}
