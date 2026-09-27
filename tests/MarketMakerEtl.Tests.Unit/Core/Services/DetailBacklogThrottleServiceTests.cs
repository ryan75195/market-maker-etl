using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DetailBacklogThrottleServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Should_report_the_full_hourly_budget_when_nothing_has_been_fetched()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime), maxFetchesPerHour: 5);

        Assert.That(throttle.RemainingHourlyBudget(), Is.EqualTo(5));
    }

    [Test]
    public void Should_reduce_the_remaining_hourly_budget_as_fetches_are_recorded()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime), maxFetchesPerHour: 5);

        throttle.RecordFetchAttempt();
        throttle.RecordFetchAttempt();

        Assert.That(throttle.RemainingHourlyBudget(), Is.EqualTo(3));
    }

    [Test]
    public void Should_restore_the_hourly_budget_once_an_hour_has_elapsed()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, maxFetchesPerHour: 2);
        throttle.RecordFetchAttempt();
        throttle.RecordFetchAttempt();
        Assert.That(throttle.RemainingHourlyBudget(), Is.EqualTo(0));

        timeProvider.Advance(TimeSpan.FromHours(1));

        Assert.That(throttle.RemainingHourlyBudget(), Is.EqualTo(2));
    }

    [Test]
    public void Should_not_be_backing_off_before_any_tick_has_been_observed()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_leave_backoff_disengaged_when_the_tick_made_no_fetch_attempts()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime));

        throttle.ObserveTickResult(new DetailBacklogTickResult(0, 0, 0, []));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_leave_backoff_disengaged_when_a_failure_is_listing_specific_rather_than_infrastructure()
    {
        var throttle = CreateThrottle(new FakeTimeProvider(StartTime));
        var listingFailure = new ScrapeRunIssueDetails("listing-1", "ItemDetailFetchFailed", "boom", "Detail", null);

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [listingFailure]));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_engage_backoff_after_a_tick_with_only_infrastructure_failures()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseSeconds: 2, backoffMaxSeconds: 30);

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.True);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.True);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_double_the_backoff_delay_for_each_further_consecutive_infrastructure_only_failure_tick()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseSeconds: 2, backoffMaxSeconds: 30);

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));
        timeProvider.Advance(TimeSpan.FromSeconds(3));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.True, "the second consecutive failure should back off for 4s, not the base 2s");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_cap_the_backoff_delay_at_the_configured_maximum()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseSeconds: 100, backoffMaxSeconds: 150);
        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));
        timeProvider.Advance(TimeSpan.FromSeconds(100));

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));
        timeProvider.Advance(TimeSpan.FromSeconds(149));
        Assert.That(
            throttle.IsBackingOffInfrastructureFailures(),
            Is.True,
            "the uncapped delay would be 200s, but it must be capped at 150s");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);
    }

    [Test]
    public void Should_reset_the_backoff_streak_after_a_tick_with_a_success()
    {
        var timeProvider = new FakeTimeProvider(StartTime);
        var throttle = CreateThrottle(timeProvider, backoffBaseSeconds: 2, backoffMaxSeconds: 30);
        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-1")]));
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 1, []));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.False);

        throttle.ObserveTickResult(new DetailBacklogTickResult(1, 1, 0, [InfrastructureFailure("listing-2")]));

        Assert.That(throttle.IsBackingOffInfrastructureFailures(), Is.True);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.That(
            throttle.IsBackingOffInfrastructureFailures(),
            Is.False,
            "the streak should restart at the base delay after a reset, not continue escalating");
    }

    private static ScrapeRunIssueDetails InfrastructureFailure(string listingId) =>
        new(listingId, ItemDetailFetchService.InfrastructureUnavailableIssueType, "sidecar unreachable", "Detail", null);

    private static DetailBacklogThrottleService CreateThrottle(
        TimeProvider timeProvider,
        int maxFetchesPerHour = 300,
        int backoffBaseSeconds = 1,
        int backoffMaxSeconds = 1800)
    {
        var options = new DetailBacklogOptions(
            Enabled: true,
            TickMinutes: 5,
            MaxFetchesPerTick: 30,
            MaxFetchesPerHour: maxFetchesPerHour,
            MaxDetailFetchAttempts: 3,
            InfrastructureBackoffBaseSeconds: backoffBaseSeconds,
            InfrastructureBackoffMaxSeconds: backoffMaxSeconds);

        return new DetailBacklogThrottleService(options, timeProvider, NullLogger<DetailBacklogThrottleService>.Instance);
    }
}
