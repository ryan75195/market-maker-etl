using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetchOutcomeMonitorTests
{
    private static readonly DateTime StartUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Should_count_recorded_outcomes_by_kind_within_the_window()
    {
        var timeProvider = new FakeTimeProvider(StartUtc);
        var monitor = new FetchOutcomeMonitor(timeProvider);

        monitor.Record(FetchOutcomeKind.Success);
        monitor.Record(FetchOutcomeKind.Success);
        monitor.Record(FetchOutcomeKind.Infrastructure);
        monitor.Record(FetchOutcomeKind.NotFound);
        monitor.Record(FetchOutcomeKind.Other);

        var snapshot = monitor.GetRecentOutcomes(TimeSpan.FromMinutes(60));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SuccessCount, Is.EqualTo(2));
            Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(1));
            Assert.That(snapshot.NotFoundFailureCount, Is.EqualTo(1));
            Assert.That(snapshot.OtherFailureCount, Is.EqualTo(1));
            Assert.That(snapshot.TotalCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void Should_exclude_outcomes_older_than_the_requested_window()
    {
        var timeProvider = new FakeTimeProvider(StartUtc);
        var monitor = new FetchOutcomeMonitor(timeProvider);

        monitor.Record(FetchOutcomeKind.Infrastructure);
        timeProvider.Advance(TimeSpan.FromMinutes(90));
        monitor.Record(FetchOutcomeKind.Success);

        var snapshot = monitor.GetRecentOutcomes(TimeSpan.FromMinutes(60));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SuccessCount, Is.EqualTo(1));
            Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(0));
        });
    }
}
