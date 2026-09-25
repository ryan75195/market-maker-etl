using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Models.Jobs;

public sealed record JobView(
    int Id,
    string SearchTerm,
    Marketplace Marketplace,
    string? FilterInstructions,
    int IntervalHours,
    bool IsEnabled,
    DateTime? LastQueuedUtc,
    DateTime? LastRunUtc,
    DateTime CreatedUtc,
    IReadOnlyList<CategoryView> Categories,
    int? ProductFamilyId);
