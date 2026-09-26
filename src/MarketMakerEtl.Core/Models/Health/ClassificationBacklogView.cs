namespace MarketMakerEtl.Core.Models.Health;

public sealed record ClassificationBacklogView(int FamilyId, string FamilyKey, int PendingCount);
