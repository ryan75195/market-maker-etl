using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class DealScanWorkerTests
{
    private static DealsOptions Options => new(10, null);

    [Test]
    public async Task Should_run_a_deal_scan_tick_without_throwing()
    {
        var deals = Substitute.For<IDealSignalService>();
        deals.ScanForDeals(Arg.Any<CancellationToken>()).Returns(new DealScanTickResult(1, 2, 1));
        var backtest = Substitute.For<IDealSignalBacktestService>();
        var worker = CreateWorker(deals, backtest);

        await worker.RunOnce(CancellationToken.None);

        await deals.Received(1).ScanForDeals(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_and_should_keep_ticking_when_the_scan_fails()
    {
        var deals = Substitute.For<IDealSignalService>();
        deals.ScanForDeals(Arg.Any<CancellationToken>())
            .Returns<Task<DealScanTickResult>>(_ => throw new InvalidOperationException("scan failed"));
        var backtest = Substitute.For<IDealSignalBacktestService>();
        var worker = CreateWorker(deals, backtest);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Should_evaluate_backtest_signals_after_scanning_for_deals()
    {
        var deals = Substitute.For<IDealSignalService>();
        deals.ScanForDeals(Arg.Any<CancellationToken>()).Returns(new DealScanTickResult(1, 2, 1));
        var backtest = Substitute.For<IDealSignalBacktestService>();
        backtest.EvaluateSignals(Arg.Any<CancellationToken>()).Returns(new DealBacktestTickResult(3, 1));
        var worker = CreateWorker(deals, backtest);

        await worker.RunOnce(CancellationToken.None);

        await backtest.Received(1).EvaluateSignals(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_and_should_keep_ticking_when_the_backtest_evaluation_fails()
    {
        var deals = Substitute.For<IDealSignalService>();
        deals.ScanForDeals(Arg.Any<CancellationToken>()).Returns(new DealScanTickResult(1, 2, 1));
        var backtest = Substitute.For<IDealSignalBacktestService>();
        backtest.EvaluateSignals(Arg.Any<CancellationToken>())
            .Returns<Task<DealBacktestTickResult>>(_ => throw new InvalidOperationException("backtest failed"));
        var worker = CreateWorker(deals, backtest);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    private static DealScanWorker CreateWorker(IDealSignalService deals, IDealSignalBacktestService backtest) =>
        new(deals, backtest, Options, TimeProvider.System, NullLogger<DealScanWorker>.Instance);
}
