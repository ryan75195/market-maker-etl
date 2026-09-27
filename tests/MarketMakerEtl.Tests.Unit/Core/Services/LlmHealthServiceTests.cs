using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class LlmHealthServiceTests
{
    private static readonly ClassifierOptions ClassifierOpts = new(5, 2000, 5);
    private static readonly OpenAiOptions OpenAiOpts = new("test-key", "gpt-6-luna", "low", 25, 6, 120);

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
        var service = new LlmHealthService(classifications, OpenAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Model, Is.EqualTo("gpt-6-luna"));
            Assert.That(health.HasApiKey, Is.True);
            Assert.That(health.LastHourSucceeded, Is.EqualTo(2));
            Assert.That(health.LastHourFailed, Is.EqualTo(1));
            Assert.That(health.Degraded, Is.False);
        });
    }

    [Test]
    public async Task Should_report_no_api_key_when_it_is_not_configured()
    {
        var classifications = Substitute.For<IListingClassificationStore>();
        classifications.GetBatchOutcomesSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        classifications.GetRecentBatchOutcomes(5, Arg.Any<CancellationToken>()).Returns([]);
        var openAiOpts = new OpenAiOptions(string.Empty, "gpt-6-luna", "low", 25, 6, 120);
        var service = new LlmHealthService(classifications, openAiOpts, ClassifierOpts);

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
        var service = new LlmHealthService(classifications, OpenAiOpts, ClassifierOpts);

        var health = await service.GetLlmHealth(CancellationToken.None);

        Assert.That(health.Degraded, Is.True);
    }
}
