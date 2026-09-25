namespace MarketMakerEtl.Core.Models.Taxonomies;

public sealed record ResolvedTaxonomyAnswer(string Question, string Choice, string? ResolvedChoice, bool IsApplicable);
