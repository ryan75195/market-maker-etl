using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Models.Health;

public sealed record FamilyBacklogHealthView(
    int FamilyId,
    string FamilyKey,
    int PendingClassificationCount,
    int NeedsReviewCount,
    FamilyState State,
    bool HasEnabledScrapeJob);
