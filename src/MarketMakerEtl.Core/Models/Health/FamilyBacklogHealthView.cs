namespace MarketMakerEtl.Core.Models.Health;

public sealed record FamilyBacklogHealthView(
    int FamilyId,
    string FamilyKey,
    int PendingClassificationCount,
    int NeedsReviewCount);
