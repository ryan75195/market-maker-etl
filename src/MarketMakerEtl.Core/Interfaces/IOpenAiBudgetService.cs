namespace MarketMakerEtl.Core.Interfaces;

public interface IOpenAiBudgetService
{
    decimal MonthlyBudgetUsd { get; }

    Task<decimal> GetMonthToDateSpend(CancellationToken ct);

    Task<bool> IsExhausted(CancellationToken ct);
}
