using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class JobStalenessRuleTests
{
    [Test]
    public void Should_not_be_stale_when_the_job_is_disabled_and_has_never_run()
    {
        var isStale = JobStalenessRule.IsStale(
            isEnabled: false, lastCompletedRunUtc: null, intervalHours: 24, nowUtc: DateTime.UtcNow);

        Assert.That(isStale, Is.False);
    }

    [Test]
    public void Should_be_stale_when_the_job_is_enabled_and_has_never_completed_a_run()
    {
        var isStale = JobStalenessRule.IsStale(
            isEnabled: true, lastCompletedRunUtc: null, intervalHours: 24, nowUtc: DateTime.UtcNow);

        Assert.That(isStale, Is.True);
    }

    [Test]
    public void Should_not_be_stale_when_the_last_completed_run_is_within_twice_the_interval()
    {
        var nowUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lastCompletedRunUtc = nowUtc.AddHours(-47);

        var isStale = JobStalenessRule.IsStale(
            isEnabled: true, lastCompletedRunUtc, intervalHours: 24, nowUtc);

        Assert.That(isStale, Is.False);
    }

    [Test]
    public void Should_be_stale_when_the_last_completed_run_is_older_than_twice_the_interval()
    {
        var nowUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lastCompletedRunUtc = nowUtc.AddHours(-49);

        var isStale = JobStalenessRule.IsStale(
            isEnabled: true, lastCompletedRunUtc, intervalHours: 24, nowUtc);

        Assert.That(isStale, Is.True);
    }
}
