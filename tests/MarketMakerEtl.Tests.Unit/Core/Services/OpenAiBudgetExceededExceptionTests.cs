using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiBudgetExceededExceptionTests
{
    [Test]
    public void Should_carry_the_monthly_budget_and_month_to_date_spend()
    {
        var exception = OpenAiBudgetExceededException.ForSpend(20m, 21.5m);

        Assert.Multiple(() =>
        {
            Assert.That(exception.MonthlyBudgetUsd, Is.EqualTo(20m));
            Assert.That(exception.MonthToDateSpendUsd, Is.EqualTo(21.5m));
        });
    }

    [Test]
    public void Should_build_a_clear_message_that_includes_the_budget_and_spend()
    {
        var exception = OpenAiBudgetExceededException.ForSpend(20m, 21.5m);

        Assert.That(exception.Message, Is.EqualTo("OpenAI monthly budget of $20.00 exhausted ($21.50 spent)."));
    }
}
