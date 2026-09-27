using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Data.Entities;

public sealed class OpenAiUsageRecordEntity
{
    public int Id { get; set; }

    public DateTime RecordedUtc { get; set; }

    public string Model { get; set; } = string.Empty;

    public OpenAiUsagePurpose Purpose { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public decimal CostUsd { get; set; }
}
