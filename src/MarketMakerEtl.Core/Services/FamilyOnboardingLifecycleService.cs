using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilyOnboardingLifecycleService : IFamilyOnboardingLifecycleService
{
    private readonly IFamilyOnboardingStore _onboarding;
    private readonly IProductFamilyStore _families;

    public FamilyOnboardingLifecycleService(IFamilyOnboardingStore onboarding, IProductFamilyStore families)
    {
        _onboarding = onboarding;
        _families = families;
    }

    public async Task<ProductFamilyView?> Approve(int familyId, CancellationToken ct)
    {
        var approved = await _onboarding.Approve(familyId, ct);
        return approved ? await _families.GetFamily(familyId, ct) : null;
    }

    public Task<bool> Reject(int familyId, CancellationToken ct) => _onboarding.Reject(familyId, ct);
}
