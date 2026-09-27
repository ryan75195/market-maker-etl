using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyOnboardingPreviewService
{
    Task RunPreview(int familyId, CancellationToken ct);

    Task<FamilyOnboardingView?> GetOnboarding(int familyId, CancellationToken ct);
}
