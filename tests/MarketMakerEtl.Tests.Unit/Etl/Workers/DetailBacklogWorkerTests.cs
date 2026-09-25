using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class DetailBacklogWorkerTests
{
    [Test]
    public async Task Should_run_a_backlog_tick_without_throwing()
    {
        var detailBacklog = Substitute.For<IDetailBacklogService>();
        detailBacklog.RunTick(Arg.Any<CancellationToken>()).Returns(new DetailBacklogTickResult(0, 0, 0, []));
        var worker = CreateWorker(detailBacklog);

        await worker.RunOnce(CancellationToken.None);

        await detailBacklog.Received(1).RunTick(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_when_the_backlog_tick_fails()
    {
        var detailBacklog = Substitute.For<IDetailBacklogService>();
        detailBacklog.RunTick(Arg.Any<CancellationToken>())
            .Returns<Task<DetailBacklogTickResult>>(_ => throw new InvalidOperationException("tick failed"));
        var worker = CreateWorker(detailBacklog);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Should_let_a_backlog_tick_run_to_completion_even_when_it_finds_failures()
    {
        var failure = new ScrapeRunIssueDetails("listing-1", "ItemDetailFetchFailed", "boom", "Detail", null);
        var detailBacklog = Substitute.For<IDetailBacklogService>();
        detailBacklog.RunTick(Arg.Any<CancellationToken>())
            .Returns(new DetailBacklogTickResult(2, 2, 1, [failure]));
        var worker = CreateWorker(detailBacklog);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
        await detailBacklog.Received(1).RunTick(Arg.Any<CancellationToken>());
    }

    private static DetailBacklogWorker CreateWorker(IDetailBacklogService detailBacklog) =>
        new(
            detailBacklog,
            new DetailBacklogOptions(Enabled: true, TickMinutes: 5, MaxFetchesPerTick: 30, MaxFetchesPerHour: 300, MaxDetailFetchAttempts: 3),
            TimeProvider.System,
            NullLogger<DetailBacklogWorker>.Instance);
}
