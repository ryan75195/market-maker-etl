using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class ClassificationWorkerTests
{
    private static ClassifierOptions Options => new("http://classifier.test", 64, 5, 2000, 120);

    [Test]
    public async Task Should_run_a_classification_tick_without_throwing()
    {
        var classification = Substitute.For<IListingClassificationService>();
        classification.ClassifyPending(Arg.Any<CancellationToken>())
            .Returns(new ClassificationTickResult(1, 2, 2, []));
        var worker = CreateWorker(classification);

        await worker.RunOnce(CancellationToken.None);

        await classification.Received(1).ClassifyPending(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_and_should_keep_ticking_when_the_classifier_is_down()
    {
        var classification = Substitute.For<IListingClassificationService>();
        classification.ClassifyPending(Arg.Any<CancellationToken>())
            .Returns<Task<ClassificationTickResult>>(_ => throw new InvalidOperationException("classifier outage"));
        var worker = CreateWorker(classification);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    private static ClassificationWorker CreateWorker(IListingClassificationService classification) =>
        new(classification, Options, TimeProvider.System, NullLogger<ClassificationWorker>.Instance);
}
