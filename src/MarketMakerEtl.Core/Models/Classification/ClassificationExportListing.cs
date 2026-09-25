namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationExportListing(
    string ListingId,
    int TaxonomyVersion,
    ListingClassificationTarget Target,
    IReadOnlyList<ListingClassificationAnswerView> Answers);
