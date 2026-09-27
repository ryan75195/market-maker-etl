using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
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
        var fetcherHealth = HealthyFetcher();
        var llmHealth = HealthyLlm();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: false, status: ScrapeRunStatus.Completed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

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
        var fetcherHealth = HealthyFetcher();
        var llmHealth = HealthyLlm();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: true, status: ScrapeRunStatus.Completed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
    }

    [Test]
    public async Task Should_report_degraded_when_a_jobs_last_run_failed()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = HealthyLlm();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: false, status: ScrapeRunStatus.Failed)]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
    }

    [Test]
    public async Task Should_report_degraded_when_the_llm_is_degraded_and_a_family_exists()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = Substitute.For<ILlmHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 0, 0, FamilyState.Active, false)]);
        llmHealth.GetLlmHealth(Arg.Any<CancellationToken>())
            .Returns(new LlmHealthView(
                "gpt-6-luna", true, 0, 5, Degraded: true, MonthToDateSpendUsd: 0m, MonthlyBudgetUsd: 20m, BudgetExhausted: false));
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(health.Llm.Degraded, Is.True);
        });
    }

    [Test]
    public async Task Should_report_ok_when_the_llm_is_degraded_but_no_family_exists()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = Substitute.For<ILlmHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        llmHealth.GetLlmHealth(Arg.Any<CancellationToken>())
            .Returns(new LlmHealthView(
                "gpt-6-luna", true, 0, 5, Degraded: true, MonthToDateSpendUsd: 0m, MonthlyBudgetUsd: 20m, BudgetExhausted: false));
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Ok));
    }

    [Test]
    public async Task Should_report_degraded_when_the_monthly_openai_budget_is_exhausted()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = Substitute.For<ILlmHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        llmHealth.GetLlmHealth(Arg.Any<CancellationToken>())
            .Returns(new LlmHealthView(
                "gpt-6-luna", true, 0, 0, Degraded: false, MonthToDateSpendUsd: 20m, MonthlyBudgetUsd: 20m, BudgetExhausted: true));
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(health.Llm.BudgetExhausted, Is.True);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_a_draft_family_has_an_enabled_scrape_job()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = HealthyLlm();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 0, 0, FamilyState.Draft, true)]);
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(health.Families.Single().State, Is.EqualTo(FamilyState.Draft));
        });
    }

    [Test]
    public async Task Should_report_ok_when_a_draft_family_has_no_enabled_scrape_job()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = HealthyLlm();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 0, 0, FamilyState.Draft, false)]);
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Ok));
    }

    [Test]
    public async Task Should_report_degraded_when_the_fetcher_health_is_degraded()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var llmHealth = HealthyLlm();
        var fetcherHealth = Substitute.For<IFetcherHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>()).Returns([]);
        fetcherHealth.GetFetcherHealth(Arg.Any<CancellationToken>())
            .Returns(new FetcherHealthView(false, 60, 0, 10, 0, 0, IsDegraded: true));
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(health.Fetcher.SidecarReachable, Is.False);
            Assert.That(health.Fetcher.RecentInfrastructureFailureCount, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task Should_build_backlogs_review_and_llm_health_from_stores()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        var familyHealth = Substitute.For<IFamilyBacklogHealthService>();
        var fetcherHealth = HealthyFetcher();
        var llmHealth = Substitute.For<ILlmHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([]);
        jobHealth.GetPendingDetailFetchCount(Arg.Any<CancellationToken>()).Returns(5);
        familyHealth.GetFamilyBacklogHealth(Arg.Any<CancellationToken>())
            .Returns([new FamilyBacklogHealthView(1, "ps5-controller", 3, 2, FamilyState.Active, false)]);
        llmHealth.GetLlmHealth(Arg.Any<CancellationToken>())
            .Returns(new LlmHealthView(
                "gpt-6-luna", true, 2, 1, Degraded: false, MonthToDateSpendUsd: 0m, MonthlyBudgetUsd: 20m, BudgetExhausted: false));
        var service = new SystemHealthService(jobHealth, familyHealth, fetcherHealth, llmHealth);

        var health = await service.GetHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Backlogs.PendingDetailFetch, Is.EqualTo(5));
            Assert.That(health.Backlogs.Classification.Single().PendingCount, Is.EqualTo(3));
            Assert.That(health.Families.Single().State, Is.EqualTo(FamilyState.Active));
            Assert.That(health.Review.Single().NeedsReviewCount, Is.EqualTo(2));
            Assert.That(health.Llm.Model, Is.EqualTo("gpt-6-luna"));
            Assert.That(health.Llm.HasApiKey, Is.True);
            Assert.That(health.Llm.LastHourSucceeded, Is.EqualTo(2));
            Assert.That(health.Llm.LastHourFailed, Is.EqualTo(1));
        });
    }

    private static IFetcherHealthService HealthyFetcher()
    {
        var fetcherHealth = Substitute.For<IFetcherHealthService>();
        fetcherHealth.GetFetcherHealth(Arg.Any<CancellationToken>())
            .Returns(new FetcherHealthView(true, 60, 1, 0, 0, 0, IsDegraded: false));
        return fetcherHealth;
    }

    private static ILlmHealthService HealthyLlm()
    {
        var llmHealth = Substitute.For<ILlmHealthService>();
        llmHealth.GetLlmHealth(Arg.Any<CancellationToken>())
            .Returns(new LlmHealthView(
                "gpt-6-luna", true, 1, 0, Degraded: false, MonthToDateSpendUsd: 0m, MonthlyBudgetUsd: 20m, BudgetExhausted: false));
        return llmHealth;
    }

    private static JobHealthView BuildJob(bool isStale, ScrapeRunStatus status) =>
        new(1, "ps5 controller", true, status, DateTime.UtcNow, DateTime.UtcNow, isStale);
}
