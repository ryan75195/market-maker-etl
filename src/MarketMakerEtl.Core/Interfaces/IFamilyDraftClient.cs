using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyDraftClient
{
    Task<FamilyDraftResult> DraftTaxonomy(FamilyDraftPrompt prompt, CancellationToken ct);
}
