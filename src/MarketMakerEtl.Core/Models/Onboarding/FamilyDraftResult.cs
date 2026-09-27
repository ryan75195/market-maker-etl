namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record FamilyDraftResult(
    string TaxonomyJson,
    string? DealGroupBy,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd);
