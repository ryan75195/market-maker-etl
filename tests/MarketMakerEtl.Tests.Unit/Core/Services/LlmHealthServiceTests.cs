using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class LlmHealthServiceTests
{
    private static readonly ClassifierOptions ClassifierOpts = new(5, 2000, 5, 5, 240);
    private static readonly OpenAiOptions OpenAiOpts = new("test-key", "gpt-6-luna", "low", 25, 6, 120);

    private static IOpenAiBudgetService NotExhaustedBudget()
    {
        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(false);
        budget.GetMonthToDateSpend(Arg.Any<CancellationToken>()).Returns(1.5m);
        budget.MonthlyBudgetUsd.Returns(20m);
        return budget;
    }

    [Test]
    public async Task Should_report_recent_success_and_failure_counts()
    {
        var classifications = Substitute.For<IListingClassificationStore>();
        classifications.GetBatchOutcomesSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ClassificationBatchRunView(DateTime.UtcNow, true),
                new ClassificationBatchRunView(DateTime.UtcNow, true),
                new ClassificationBatchRunView(DateTime.UtcNow, false)
            ]);
        classifications.GetRecentBatchOutcomes(5, Arg.Any<CancellationToken>())
            .Returns([new ClassificationBatchRunView(DateTime.UtcNow, true)]);
        var service = new LlmHealthService(classifications, NotExhaustedBudget(), OpenAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Model, Is.EqualTo("gpt-6-luna"));
            Assert.That(health.HasApiKey, Is.True);
            Assert.That(health.LastHourSucceeded, Is.EqualTo(2));
            Assert.That(health.LastHourFailed, Is.EqualTo(1));
            Assert.That(health.Degraded, Is.False);
            Assert.That(health.MonthToDateSpendUsd, Is.EqualTo(1.5m));
            Assert.That(health.MonthlyBudgetUsd, Is.EqualTo(20m));
            Assert.That(health.BudgetExhausted, Is.False);
        });
    }

    [Test]
    public async Task Should_report_no_api_key_when_it_is_not_configured()
    {
        var classifications = Substitute.For<IListingClassificationStore>();
        classifications.GetBatchOutcomesSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        classifications.GetRecentBatchOutcomes(5, Arg.Any<CancellationToken>()).Returns([]);
        var openAiOpts = new OpenAiOptions(string.Empty, "gpt-6-luna", "low", 25, 6, 120);
        var service = new LlmHealthService(classifications, NotExhaustedBudget(), openAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.That(health.HasApiKey, Is.False);
    }

    [Test]
    public async Task Should_report_degraded_when_all_recent_batches_up_to_the_threshold_failed()
    {
        var classifications = Substitute.For<IListingClassificationStore>();
        classifications.GetBatchOutcomesSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        classifications.GetRecentBatchOutcomes(5, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 5).Select(_ => new ClassificationBatchRunView(DateTime.UtcNow, false)).ToList());
        var service = new LlmHealthService(classifications, NotExhaustedBudget(), OpenAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.That(health.Degraded, Is.True);
    }

    [Test]
    public async Task Should_report_budget_exhausted_when_the_budget_service_reports_it()
    {
        var classifications = Substitute.For<IListingClassificationStore>();
        classifications.GetBatchOutcomesSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        classifications.GetRecentBatchOutcomes(5, Arg.Any<CancellationToken>()).Returns([]);
        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(true);
        budget.GetMonthToDateSpend(Arg.Any<CancellationToken>()).Returns(20m);
        budget.MonthlyBudgetUsd.Returns(20m);
        var service = new LlmHealthService(classifications, budget, OpenAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.That(health.BudgetExhausted, Is.True);
    }
}
