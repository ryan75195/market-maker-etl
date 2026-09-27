using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetcherHealthServiceTests
{
    [Test]
    public async Task Should_report_healthy_when_reachable_and_recent_fetches_succeeded()
    {
        var client = Substitute.For<IFetcherHealthClient>();
        var outcomeStore = Substitute.For<IFetchOutcomeStore>();
        client.CheckSidecarReachable(Arg.Any<CancellationToken>()).Returns(true);
        outcomeStore.GetRecentOutcomes(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new FetchOutcomeSnapshot(5, 0, 0, 0));
        var service = new FetcherHealthService(client, outcomeStore, new FetcherHealthOptions(60, 10));

        var health = await service.GetFetcherHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.SidecarReachable, Is.True);
            Assert.That(health.IsDegraded, Is.False);
            Assert.That(health.RecentSuccessCount, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_sidecar_is_unreachable()
    {
        var client = Substitute.For<IFetcherHealthClient>();
        var outcomeStore = Substitute.For<IFetchOutcomeStore>();
        client.CheckSidecarReachable(Arg.Any<CancellationToken>()).Returns(false);
        outcomeStore.GetRecentOutcomes(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new FetchOutcomeSnapshot(1, 0, 0, 0));
        var service = new FetcherHealthService(client, outcomeStore, new FetcherHealthOptions(60, 10));

        var health = await service.GetFetcherHealth(CancellationToken.None);

        Assert.That(health.IsDegraded, Is.True);
    }

    [Test]
    public async Task Should_report_degraded_when_the_recent_window_has_enough_attempts_and_no_successes()
    {
        var client = Substitute.For<IFetcherHealthClient>();
        var outcomeStore = Substitute.For<IFetchOutcomeStore>();
        client.CheckSidecarReachable(Arg.Any<CancellationToken>()).Returns(true);
        outcomeStore.GetRecentOutcomes(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new FetchOutcomeSnapshot(0, 7, 3, 0));
        var service = new FetcherHealthService(client, outcomeStore, new FetcherHealthOptions(60, 10));

        var health = await service.GetFetcherHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.IsDegraded, Is.True);
            Assert.That(health.RecentInfrastructureFailureCount, Is.EqualTo(7));
            Assert.That(health.RecentNotFoundFailureCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Should_report_healthy_when_failures_have_not_yet_reached_the_degraded_threshold()
    {
        var client = Substitute.For<IFetcherHealthClient>();
        var outcomeStore = Substitute.For<IFetchOutcomeStore>();
        client.CheckSidecarReachable(Arg.Any<CancellationToken>()).Returns(true);
        outcomeStore.GetRecentOutcomes(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new FetchOutcomeSnapshot(0, 4, 0, 0));
        var service = new FetcherHealthService(client, outcomeStore, new FetcherHealthOptions(60, 10));

        var health = await service.GetFetcherHealth(CancellationToken.None);

        Assert.That(health.IsDegraded, Is.False);
    }
}
