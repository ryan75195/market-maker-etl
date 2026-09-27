using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class OpenAiUsageStore : IOpenAiUsageStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public OpenAiUsageStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task Record(
        string model, OpenAiUsagePurpose purpose, int promptTokens, int completionTokens, decimal costUsd, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.OpenAiUsageRecords.Add(new OpenAiUsageRecordEntity
        {
            RecordedUtc = DateTime.UtcNow,
            Model = model,
            Purpose = purpose,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CostUsd = costUsd
        });
        await SqliteBusyRetry.ExecuteAsync(() => db.SaveChangesAsync(ct), ct);
    }

    public async Task<decimal> GetSpendSince(DateTime sinceUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OpenAiUsageRecords
            .Where(r => r.RecordedUtc >= sinceUtc)
            .SumAsync(r => (decimal?)r.CostUsd, ct) ?? 0m;
    }
}
