using System.Globalization;

namespace MarketMakerEtl.Core.Services;

public sealed class OpenAiBudgetExceededException : Exception
{
    public decimal MonthlyBudgetUsd { get; private init; }

    public decimal MonthToDateSpendUsd { get; private init; }

    public OpenAiBudgetExceededException()
    {
    }

    public OpenAiBudgetExceededException(string message)
        : base(message)
    {
    }

    public OpenAiBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static OpenAiBudgetExceededException ForSpend(decimal monthlyBudgetUsd, decimal monthToDateSpendUsd) =>
        new(BuildMessage(monthlyBudgetUsd, monthToDateSpendUsd))
        {
            MonthlyBudgetUsd = monthlyBudgetUsd,
            MonthToDateSpendUsd = monthToDateSpendUsd
        };

    private static string BuildMessage(decimal monthlyBudgetUsd, decimal monthToDateSpendUsd) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"OpenAI monthly budget of ${monthlyBudgetUsd:F2} exhausted (${monthToDateSpendUsd:F2} spent).");
}
