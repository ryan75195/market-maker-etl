using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DealSignalBacktestServiceTests
{
    private static readonly DateTime CreatedUtc = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Should_evaluate_a_pending_signal_using_the_base_horizon_window_and_store_the_margin()
    {
        var store = Substitute.For<IDealSignalBacktestStore>();
        var priceGroups = Substitute.For<IPriceGroupQueryService>();
        var candidate = BuildCandidate(evaluationWindowDays: null);
        SetUpPending(store, candidate);
        priceGroups.GetForwardWindowStats(Arg.Any<PriceGroupForwardWindowQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PriceGroupForwardWindowResult(5, 120m, false));
        store.GetListingSaleInfo(candidate.ListingEntityId, Arg.Any<CancellationToken>())
            .Returns((DealSignalListingSaleInfo?)null);
        var service = CreateService(store, priceGroups);

        var result = await service.EvaluateSignals(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.SignalsEvaluated, Is.EqualTo(1));
            Assert.That(result.SignalsFinalized, Is.EqualTo(1));
        });
        await priceGroups.Received(1).GetForwardWindowStats(
            Arg.Is<PriceGroupForwardWindowQuery>(q =>
                q.WindowStartExclusiveUtc == CreatedUtc && q.WindowEndInclusiveUtc == CreatedUtc.AddDays(14)),
            Arg.Any<CancellationToken>());
        await store.Received(1).ApplyEvaluations(
            Arg.Is<IReadOnlyList<DealSignalEvaluationResult>>(rs =>
                rs.Single().ForwardNetMedian == 120m
                && rs.Single().RealisedMargin == 120m - candidate.LandedPrice
                && rs.Single().EvaluationWindowDays == 14),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_reevaluate_a_thin_signal_using_the_window_extended_to_twice_the_horizon()
    {
        var store = Substitute.For<IDealSignalBacktestStore>();
        var priceGroups = Substitute.For<IPriceGroupQueryService>();
        var candidate = BuildCandidate(evaluationWindowDays: 14);
        SetUpPending(store, candidate);
        priceGroups.GetForwardWindowStats(Arg.Any<PriceGroupForwardWindowQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PriceGroupForwardWindowResult(2, 90m, false));
        store.GetListingSaleInfo(candidate.ListingEntityId, Arg.Any<CancellationToken>())
            .Returns((DealSignalListingSaleInfo?)null);
        var service = CreateService(store, priceGroups);

        var result = await service.EvaluateSignals(CancellationToken.None);

        Assert.That(result.SignalsFinalized, Is.EqualTo(0));
        await priceGroups.Received(1).GetForwardWindowStats(
            Arg.Is<PriceGroupForwardWindowQuery>(q => q.WindowEndInclusiveUtc == CreatedUtc.AddDays(28)),
            Arg.Any<CancellationToken>());
        await store.Received(1).ApplyEvaluations(
            Arg.Is<IReadOnlyList<DealSignalEvaluationResult>>(rs => rs.Single().EvaluationWindowDays == 28),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_mark_used_estimated_dates_when_the_flagged_listing_sold_date_is_estimated()
    {
        var store = Substitute.For<IDealSignalBacktestStore>();
        var priceGroups = Substitute.For<IPriceGroupQueryService>();
        var candidate = BuildCandidate(evaluationWindowDays: null);
        SetUpPending(store, candidate);
        priceGroups.GetForwardWindowStats(Arg.Any<PriceGroupForwardWindowQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PriceGroupForwardWindowResult(3, 100m, false));
        store.GetListingSaleInfo(candidate.ListingEntityId, Arg.Any<CancellationToken>())
            .Returns(new DealSignalListingSaleInfo(true, CreatedUtc.AddHours(10), true));
        var service = CreateService(store, priceGroups);

        await service.EvaluateSignals(CancellationToken.None);

        await store.Received(1).ApplyEvaluations(
            Arg.Is<IReadOnlyList<DealSignalEvaluationResult>>(rs =>
                rs.Single().UsedEstimatedDates && rs.Single().ListingSoldWithinHours == 10d),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_leave_realised_margin_null_when_the_forward_window_has_no_sales()
    {
        var store = Substitute.For<IDealSignalBacktestStore>();
        var priceGroups = Substitute.For<IPriceGroupQueryService>();
        var candidate = BuildCandidate(evaluationWindowDays: null);
        SetUpPending(store, candidate);
        priceGroups.GetForwardWindowStats(Arg.Any<PriceGroupForwardWindowQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PriceGroupForwardWindowResult(0, null, false));
        store.GetListingSaleInfo(candidate.ListingEntityId, Arg.Any<CancellationToken>())
            .Returns((DealSignalListingSaleInfo?)null);
        var service = CreateService(store, priceGroups);

        var result = await service.EvaluateSignals(CancellationToken.None);

        Assert.That(result.SignalsFinalized, Is.EqualTo(0));
        await store.Received(1).ApplyEvaluations(
            Arg.Is<IReadOnlyList<DealSignalEvaluationResult>>(rs => rs.Single().RealisedMargin == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_do_nothing_when_there_are_no_signals_pending_evaluation()
    {
        var store = Substitute.For<IDealSignalBacktestStore>();
        var priceGroups = Substitute.For<IPriceGroupQueryService>();
        store.GetSignalsPendingEvaluation(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<DealSignalEvaluationCandidate>());
        var service = CreateService(store, priceGroups);

        var result = await service.EvaluateSignals(CancellationToken.None);

        Assert.That(result.SignalsEvaluated, Is.EqualTo(0));
        await store.DidNotReceive().ApplyEvaluations(
            Arg.Any<IReadOnlyList<DealSignalEvaluationResult>>(), Arg.Any<CancellationToken>());
    }

    private static void SetUpPending(IDealSignalBacktestStore store, DealSignalEvaluationCandidate candidate) =>
        store.GetSignalsPendingEvaluation(Arg.Any<DateTime>(), 14, 3, Arg.Any<CancellationToken>())
            .Returns(new List<DealSignalEvaluationCandidate> { candidate });

    private static DealSignalEvaluationCandidate BuildCandidate(int? evaluationWindowDays) => new(
        1,
        10,
        2,
        new Dictionary<string, string> { ["model"] = "dualsense" },
        70m,
        CreatedUtc,
        evaluationWindowDays);

    private static DealSignalBacktestService CreateService(
        IDealSignalBacktestStore store, IPriceGroupQueryService priceGroups) =>
        new(store, priceGroups, new BacktestOptions(14), new FakeTimeProvider(Now));
}
