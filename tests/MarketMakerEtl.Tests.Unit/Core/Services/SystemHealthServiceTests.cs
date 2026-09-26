using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SystemHealthServiceTests
{
    [Test]
    public async Task Should_report_ok_when_no_job_is_stale_or_failed_and_no_family_exists()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: false, status: ScrapeRunStatus.Completed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", false, []));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Ok));
            Assert.That(health.DatabaseReachable, Is.True);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_a_job_is_stale()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: true, status: ScrapeRunStatus.Completed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", false, []));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
    }

    [Test]
    public async Task Should_report_degraded_when_a_jobs_last_run_failed()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: false, status: ScrapeRunStatus.Failed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", false, []));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
    }

    [Test]
    public async Task Should_report_degraded_when_the_classifier_is_unreachable_and_a_family_exists()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 0, 0)]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", false, []));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
    }

    [Test]
    public async Task Should_report_ok_when_the_classifier_is_unreachable_but_no_family_exists()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", false, []));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Ok));
    }

    [Test]
    public async Task Should_build_backlogs_and_review_from_family_health()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var classifier = Substitute.For<IListingClassifierClient>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        jobHealth.GetPendingDetailFetchCount(Arg.Any<CancellationToken>()).Returns(5);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 3, 2)]);
        classifier.CheckHealth(Arg.Any<CancellationToken>()).Returns(new ClassifierHealthCheckResult("http://classifier.test", true, ["ps5-controller"]));
        var service = new SystemHealthService(jobHealth, familyHealth, classifier);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Backlogs.PendingDetailFetch, Is.EqualTo(5));
            Assert.That(health.Backlogs.Classification.Single().PendingCount, Is.EqualTo(3));
            Assert.That(health.Review.Single().NeedsReviewCount, Is.EqualTo(2));
            Assert.That(health.Classifier.LoadedModels, Does.Contain("ps5-controller"));
        });
    }

    private static JobHealthView BuildJob(bool isStale, ScrapeRunStatus status) =>
        new(1, "ps5 controller", true, status, DateTime.UtcNow, DateTime.UtcNow, isStale);
}
