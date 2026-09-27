using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyOnboardingDraftingService
{
    Task<ProductFamilyView?> StartOnboarding(
        string name, string searchTerm, string? key, CancellationToken ct);

    Task<ProductFamilyView?> Regenerate(int familyId, string feedback, CancellationToken ct);
}
