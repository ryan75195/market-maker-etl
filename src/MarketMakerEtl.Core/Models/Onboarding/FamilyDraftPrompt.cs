namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record FamilyDraftPrompt(
    string FamilyName,
    string SearchTerm,
    IReadOnlyList<FamilySampleListing> Sample,
    string? PreviousTaxonomyJson,
    string? Feedback);
