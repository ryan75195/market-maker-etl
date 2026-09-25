namespace MarketMakerEtl.Core.Models.Families;

public sealed record TaxonomyVersionView(
    int Id,
    int ProductFamilyId,
    int Version,
    string QuestionsJson,
    DateTime CreatedUtc);
