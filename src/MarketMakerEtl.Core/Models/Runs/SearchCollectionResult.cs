using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Models.Runs;

public sealed record SearchCollectionResult(
    IReadOnlyList<ListingSummary> Listings,
    int? TotalReportedBySearch,
    IReadOnlyList<ScrapeRunIssueDetails> Issues,
    int BackfillItemPageFetches = 0);
