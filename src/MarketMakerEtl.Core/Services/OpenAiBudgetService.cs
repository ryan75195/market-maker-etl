using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

public sealed class OpenAiBudgetService : IOpenAiBudgetService
{
    private readonly IOpenAiUsageStore _usage;
    private readonly OpenAiBudgetOptions _options;
    private readonly TimeProvider _timeProvider;

    public OpenAiBudgetService(IOpenAiUsageStore usage, OpenAiBudgetOptions options, TimeProvider timeProvider)
    {
        _usage = usage;
        _options = options;
        _timeProvider = timeProvider;
    }

    public decimal MonthlyBudgetUsd => _options.MonthlyBudgetUsd;

    public Task<decimal> GetMonthToDateSpend(CancellationToken ct) =>
        _usage.GetSpendSince(StartOfMonthUtc(), ct);

    public async Task<bool> IsExhausted(CancellationToken ct) =>
        await GetMonthToDateSpend(ct) >= _options.MonthlyBudgetUsd;

    private DateTime StartOfMonthUtc()
    {
        var now = _timeProvider.GetUtcNow();
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
