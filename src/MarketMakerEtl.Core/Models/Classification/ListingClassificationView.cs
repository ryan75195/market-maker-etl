namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ListingClassificationView(
    int ListingEntityId,
    int TaxonomyVersion,
    IReadOnlyList<ListingClassificationAnswerView> Answers);
