namespace MarketMakerEtl.Core.Models.Taxonomies;

public sealed record TaxonomyValidationResult(bool IsValid, IReadOnlyList<string> Errors);
