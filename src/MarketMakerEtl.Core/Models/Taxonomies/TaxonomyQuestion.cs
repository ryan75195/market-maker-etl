namespace MarketMakerEtl.Core.Models.Taxonomies;

public sealed record TaxonomyQuestion(
    string Key,
    string Instructions,
    IReadOnlyDictionary<string, string> Criteria,
    IReadOnlyList<TaxonomyAskWhenClause> AskWhen,
    string? NotStatedMeans);
