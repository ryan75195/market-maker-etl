using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFamilyOnboardingStore
{
    Task Create(
        int familyId,
        int jobId,
        string searchTerm,
        IReadOnlyList<FamilySampleListing> sample,
        int promptTokens,
        int completionTokens,
        decimal costUsd,
        CancellationToken ct);

    Task RecordRedraft(
        int familyId, string feedback, int promptTokens, int completionTokens, decimal costUsd, CancellationToken ct);

    Task SavePreview(int familyId, IReadOnlyList<OnboardingQuestionDistributionView> preview, CancellationToken ct);

    Task<FamilyOnboardingSnapshot?> GetSnapshot(int familyId, CancellationToken ct);

    Task<bool> Approve(int familyId, CancellationToken ct);

    Task<bool> Reject(int familyId, CancellationToken ct);
}
