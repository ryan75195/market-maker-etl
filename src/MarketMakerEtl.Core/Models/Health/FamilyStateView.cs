using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Models.Health;

public sealed record FamilyStateView(int FamilyId, string FamilyKey, FamilyState State);
