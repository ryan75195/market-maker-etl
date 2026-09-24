using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ScrapeRunStateServiceTests
{
    private static readonly ScrapeRunStateService Service = new();

    [Test]
    public void Should_permit_the_legal_state_transitions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Service.EnsureCanTransition(ScrapeRunStatus.Queued, ScrapeRunStatus.Running), Throws.Nothing);
            Assert.That(() => Service.EnsureCanTransition(ScrapeRunStatus.Running, ScrapeRunStatus.Completed), Throws.Nothing);
            Assert.That(() => Service.EnsureCanTransition(ScrapeRunStatus.Running, ScrapeRunStatus.CompletedWithErrors), Throws.Nothing);
            Assert.That(() => Service.EnsureCanTransition(ScrapeRunStatus.Running, ScrapeRunStatus.Failed), Throws.Nothing);
        });
    }

    [Test]
    public void Should_reject_an_illegal_state_transition()
    {
        Assert.That(
            () => Service.EnsureCanTransition(ScrapeRunStatus.Completed, ScrapeRunStatus.Running),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Should_treat_completed_and_failed_as_terminal()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Service.IsTerminal(ScrapeRunStatus.Completed), Is.True);
            Assert.That(Service.IsTerminal(ScrapeRunStatus.CompletedWithErrors), Is.True);
            Assert.That(Service.IsTerminal(ScrapeRunStatus.Failed), Is.True);
            Assert.That(Service.IsTerminal(ScrapeRunStatus.Running), Is.False);
        });
    }

    [Test]
    public void Should_reject_leaving_completed_with_errors()
    {
        Assert.That(
            () => Service.EnsureCanTransition(ScrapeRunStatus.CompletedWithErrors, ScrapeRunStatus.Running),
            Throws.TypeOf<InvalidOperationException>());
    }
}
