namespace MarketMakerEtl.Core.Models.Taxonomies;

public sealed record TaxonomyDocument(string Family, int Version, IReadOnlyList<TaxonomyQuestion> Questions);
