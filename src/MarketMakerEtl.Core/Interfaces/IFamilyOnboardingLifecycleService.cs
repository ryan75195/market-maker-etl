using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyOnboardingLifecycleService
{
    Task<ProductFamilyView?> Approve(int familyId, CancellationToken ct);

    Task<bool> Reject(int familyId, CancellationToken ct);
}
