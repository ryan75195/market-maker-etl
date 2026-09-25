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
        classification.ClassifyPending(Arg.Any<Action<ClassificationBatchFailure>>(), Arg.Any<CancellationToken>())
            .Returns(new ClassificationTickResult(1, 2, 2, []));
        var worker = CreateWorker(classification);

        await worker.RunOnce(CancellationToken.None);

        await classification.Received(1)
            .ClassifyPending(Arg.Any<Action<ClassificationBatchFailure>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_invoke_the_reported_batch_failure_callback_without_throwing()
    {
        var classification = Substitute.For<IListingClassificationService>();
        var failure = new ClassificationBatchFailure(10, 5, "classifier timed out after 120s.");
        classification
            .ClassifyPending(
                Arg.Do<Action<ClassificationBatchFailure>>(onBatchFailure => onBatchFailure(failure)),
                Arg.Any<CancellationToken>())
            .Returns(new ClassificationTickResult(1, 5, 0, [failure]));
        var worker = CreateWorker(classification);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public void Should_not_throw_and_should_keep_ticking_when_the_classifier_is_down()
    {
        var classification = Substitute.For<IListingClassificationService>();
        classification.ClassifyPending(Arg.Any<Action<ClassificationBatchFailure>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClassificationTickResult>>(_ => throw new InvalidOperationException("classifier outage"));
        var worker = CreateWorker(classification);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    private static ClassificationWorker CreateWorker(IListingClassificationService classification) =>
        new(classification, Options, TimeProvider.System, NullLogger<ClassificationWorker>.Instance);
}
