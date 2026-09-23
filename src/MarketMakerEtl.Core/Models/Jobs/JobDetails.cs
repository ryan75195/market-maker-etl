using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Models.Jobs;

public sealed record JobDetails(
    string SearchTerm,
    Marketplace Marketplace,
    string? FilterInstructions,
    int IntervalHours,
    bool IsEnabled,
    IReadOnlyList<int> CategoryIds);
