using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class ScrapeWorkerTests
{
    [Test]
    public async Task Should_run_the_claimed_work_item()
    {
        var store = Substitute.For<IScrapeStore>();
        store.ClaimNextQueuedRun(Arg.Any<CancellationToken>()).Returns(new ScrapeRunWork(1, 2, "ps5"));
        var runs = Substitute.For<IScrapeRunService>();
        var worker = new ScrapeWorker(store, runs, NullLogger<ScrapeWorker>.Instance);

        var processed = await worker.RunOnce(CancellationToken.None);

        await runs.Received(1).Run(Arg.Is<ScrapeRunWork>(w => w.RunId == 1), Arg.Any<CancellationToken>());
        Assert.That(processed, Is.True);
    }

    [Test]
    public async Task Should_report_no_work_when_the_queue_is_empty()
    {
        var store = Substitute.For<IScrapeStore>();
        store.ClaimNextQueuedRun(Arg.Any<CancellationToken>()).Returns((ScrapeRunWork?)null);
        var runs = Substitute.For<IScrapeRunService>();
        var worker = new ScrapeWorker(store, runs, NullLogger<ScrapeWorker>.Instance);

        var processed = await worker.RunOnce(CancellationToken.None);

        await runs.DidNotReceive().Run(Arg.Any<ScrapeRunWork>(), Arg.Any<CancellationToken>());
        Assert.That(processed, Is.False);
    }
}
