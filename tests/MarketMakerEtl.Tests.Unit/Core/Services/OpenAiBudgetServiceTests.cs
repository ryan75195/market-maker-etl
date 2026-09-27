using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiBudgetServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Should_expose_the_configured_monthly_budget()
    {
        var service = CreateService(Substitute.For<IOpenAiUsageStore>(), 20m);

        Assert.That(service.MonthlyBudgetUsd, Is.EqualTo(20m));
    }

    [Test]
    public async Task Should_report_the_spend_recorded_since_the_start_of_the_current_month()
    {
        var usage = Substitute.For<IOpenAiUsageStore>();
        usage.GetSpendSince(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>())
            .Returns(5.5m);
        var service = CreateService(usage, 20m);

        var spend = await service.GetMonthToDateSpend(CancellationToken.None);

        Assert.That(spend, Is.EqualTo(5.5m));
    }

    [Test]
    public async Task Should_report_not_exhausted_when_spend_is_below_the_budget()
    {
        var usage = Substitute.For<IOpenAiUsageStore>();
        usage.GetSpendSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(19.99m);
        var service = CreateService(usage, 20m);

        var exhausted = await service.IsExhausted(CancellationToken.None);

        Assert.That(exhausted, Is.False);
    }

    [Test]
    public async Task Should_report_exhausted_when_spend_meets_the_budget()
    {
        var usage = Substitute.For<IOpenAiUsageStore>();
        usage.GetSpendSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(20m);
        var service = CreateService(usage, 20m);

        var exhausted = await service.IsExhausted(CancellationToken.None);

        Assert.That(exhausted, Is.True);
    }

    [Test]
    public async Task Should_report_exhausted_when_spend_exceeds_the_budget()
    {
        var usage = Substitute.For<IOpenAiUsageStore>();
        usage.GetSpendSince(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(25m);
        var service = CreateService(usage, 20m);

        var exhausted = await service.IsExhausted(CancellationToken.None);

        Assert.That(exhausted, Is.True);
    }

    private static OpenAiBudgetService CreateService(IOpenAiUsageStore usage, decimal monthlyBudgetUsd) =>
        new(usage, new OpenAiBudgetOptions(monthlyBudgetUsd), new FakeTimeProvider(Now));
}
