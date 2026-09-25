namespace MarketMakerEtl.Core.Models.Taxonomies;

public sealed record TaxonomyAskWhenClause(string Question, IReadOnlyList<string> AnyOf);
