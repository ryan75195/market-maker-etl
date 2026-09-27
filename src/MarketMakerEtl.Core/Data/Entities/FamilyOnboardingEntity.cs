namespace MarketMakerEtl.Core.Data.Entities;

public sealed class FamilyOnboardingEntity
{
    public int Id { get; set; }

    public int ProductFamilyId { get; set; }

    public int JobId { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    public string SampleListingsJson { get; set; } = string.Empty;

    public string? PreviewJson { get; set; }

    public string? LastFeedback { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public decimal CostUsd { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public ProductFamilyEntity? ProductFamily { get; set; }
}
