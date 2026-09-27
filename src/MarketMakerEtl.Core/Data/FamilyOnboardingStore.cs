using System.Text.Json;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class FamilyOnboardingStore : IFamilyOnboardingStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public FamilyOnboardingStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task Create(
        int familyId,
        int jobId,
        string searchTerm,
        IReadOnlyList<FamilySampleListing> sample,
        int promptTokens,
        int completionTokens,
        decimal costUsd,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.FamilyOnboardings.Add(new FamilyOnboardingEntity
        {
            ProductFamilyId = familyId,
            JobId = jobId,
            SearchTerm = searchTerm,
            SampleListingsJson = JsonSerializer.Serialize(sample),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CostUsd = costUsd,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordRedraft(
        int familyId, string feedback, int promptTokens, int completionTokens, decimal costUsd, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var onboarding = await FindOnboarding(db, familyId, ct);
        if (onboarding is null)
        {
            return;
        }

        onboarding.LastFeedback = feedback;
        onboarding.PromptTokens += promptTokens;
        onboarding.CompletionTokens += completionTokens;
        onboarding.CostUsd += costUsd;
        onboarding.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SavePreview(
        int familyId, IReadOnlyList<OnboardingQuestionDistributionView> preview, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var onboarding = await FindOnboarding(db, familyId, ct);
        if (onboarding is null)
        {
            return;
        }

        onboarding.PreviewJson = JsonSerializer.Serialize(preview);
        onboarding.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FamilyOnboardingSnapshot?> GetSnapshot(int familyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var onboarding = await FindOnboarding(db, familyId, ct);
        return onboarding is null ? null : MapToSnapshot(onboarding);
    }

    public async Task<bool> Approve(int familyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var onboarding = await FindOnboarding(db, familyId, ct);
        var family = await db.ProductFamilies.FindAsync([familyId], ct);
        if (onboarding is null || family is null)
        {
            return false;
        }

        var job = await db.ScrapeJobs.FindAsync([onboarding.JobId], ct);
        family.State = FamilyState.Active;
        if (job is not null)
        {
            job.IsEnabled = true;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> Reject(int familyId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var onboarding = await FindOnboarding(db, familyId, ct);
        var family = await db.ProductFamilies.FindAsync([familyId], ct);
        if (onboarding is null || family is null)
        {
            return false;
        }

        var job = await db.ScrapeJobs.FindAsync([onboarding.JobId], ct);
        if (job is not null)
        {
            db.ScrapeJobs.Remove(job);
        }

        db.ProductFamilies.Remove(family);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Task<FamilyOnboardingEntity?> FindOnboarding(
        EtlDbContext db, int familyId, CancellationToken ct) =>
        db.FamilyOnboardings.FirstOrDefaultAsync(o => o.ProductFamilyId == familyId, ct);

    private static FamilyOnboardingSnapshot MapToSnapshot(FamilyOnboardingEntity onboarding) =>
        new(
            onboarding.ProductFamilyId,
            onboarding.JobId,
            onboarding.SearchTerm,
            JsonSerializer.Deserialize<IReadOnlyList<FamilySampleListing>>(onboarding.SampleListingsJson) ?? [],
            onboarding.PreviewJson is null
                ? []
                : JsonSerializer.Deserialize<IReadOnlyList<OnboardingQuestionDistributionView>>(onboarding.PreviewJson) ?? [],
            onboarding.PromptTokens,
            onboarding.CompletionTokens,
            onboarding.CostUsd,
            onboarding.LastFeedback,
            onboarding.UpdatedUtc);
}
