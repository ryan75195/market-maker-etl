using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class FamilyOnboardingStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private FamilyOnboardingStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-family-onboarding-store-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        _store = new FamilyOnboardingStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_create_and_retrieve_an_onboarding_snapshot()
    {
        var (familyId, jobId) = await SeedDraftFamilyWithJob();
        var sample = new[] { new FamilySampleListing("m1", "Title", "Desc", "Cat", "Brand", false, 10m, "https://example.test/1") };

        await _store.Create(familyId, jobId, "ps5 controller", sample, 100, 200, 0.01m, CancellationToken.None);
        var snapshot = await _store.GetSnapshot(familyId, CancellationToken.None);

        Assert.That(snapshot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.SearchTerm, Is.EqualTo("ps5 controller"));
            Assert.That(snapshot.Sample, Has.Count.EqualTo(1));
            Assert.That(snapshot.PromptTokens, Is.EqualTo(100));
            Assert.That(snapshot.CompletionTokens, Is.EqualTo(200));
            Assert.That(snapshot.CostUsd, Is.EqualTo(0.01m));
        });
    }

    [Test]
    public async Task Should_accumulate_token_usage_and_cost_across_redrafts()
    {
        var (familyId, jobId) = await SeedDraftFamilyWithJob();
        await _store.Create(familyId, jobId, "ps5 controller", [], 100, 200, 0.01m, CancellationToken.None);

        await _store.RecordRedraft(familyId, "make it stricter", 50, 60, 0.005m, CancellationToken.None);
        var snapshot = await _store.GetSnapshot(familyId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.PromptTokens, Is.EqualTo(150));
            Assert.That(snapshot.CompletionTokens, Is.EqualTo(260));
            Assert.That(snapshot.CostUsd, Is.EqualTo(0.015m));
            Assert.That(snapshot.LastFeedback, Is.EqualTo("make it stricter"));
        });
    }

    [Test]
    public async Task Should_save_and_return_the_preview_distribution()
    {
        var (familyId, jobId) = await SeedDraftFamilyWithJob();
        await _store.Create(familyId, jobId, "ps5 controller", [], 0, 0, 0m, CancellationToken.None);
        var preview = new[]
        {
            new OnboardingQuestionDistributionView(
                "item_type",
                [new OnboardingChoiceDistributionView("controller", 3, [])])
        };

        await _store.SavePreview(familyId, preview, CancellationToken.None);
        var snapshot = await _store.GetSnapshot(familyId, CancellationToken.None);

        Assert.That(snapshot!.Preview, Has.Count.EqualTo(1));
        Assert.That(snapshot.Preview[0].Question, Is.EqualTo("item_type"));
    }

    [Test]
    public async Task Should_activate_the_family_and_enable_the_job_on_approve()
    {
        var (familyId, jobId) = await SeedDraftFamilyWithJob();
        await _store.Create(familyId, jobId, "ps5 controller", [], 0, 0, 0m, CancellationToken.None);

        var approved = await _store.Approve(familyId, CancellationToken.None);

        Assert.That(approved, Is.True);
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var family = await db.ProductFamilies.FindAsync(familyId);
        var job = await db.ScrapeJobs.FindAsync(jobId);
        Assert.Multiple(() =>
        {
            Assert.That(family!.State, Is.EqualTo(FamilyState.Active));
            Assert.That(job!.IsEnabled, Is.True);
        });
    }

    [Test]
    public async Task Should_delete_the_family_job_and_taxonomy_on_reject()
    {
        var (familyId, jobId) = await SeedDraftFamilyWithJob();
        await _store.Create(familyId, jobId, "ps5 controller", [], 0, 0, 0m, CancellationToken.None);

        var rejected = await _store.Reject(familyId, CancellationToken.None);

        Assert.That(rejected, Is.True);
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        Assert.Multiple(() =>
        {
            Assert.That(db.ProductFamilies.Find(familyId), Is.Null);
            Assert.That(db.ScrapeJobs.Find(jobId), Is.Null);
        });
    }

    [Test]
    public async Task Should_return_null_snapshot_when_no_onboarding_row_exists()
    {
        var snapshot = await _store.GetSnapshot(999, CancellationToken.None);

        Assert.That(snapshot, Is.Null);
    }

    private async Task<SeededFamily> SeedDraftFamilyWithJob()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var family = new ProductFamilyEntity
        {
            Key = "ps5-controller",
            Name = "PS5 Controller",
            ModelName = "ps5-controller",
            State = FamilyState.Draft,
            CreatedUtc = DateTime.UtcNow
        };
        var job = new ScrapeJobEntity
        {
            SearchTerm = "ps5 controller",
            Marketplace = Marketplace.Mercari,
            IsEnabled = false,
            CreatedUtc = DateTime.UtcNow
        };
        db.ProductFamilies.Add(family);
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        job.ProductFamilyId = family.Id;
        await db.SaveChangesAsync();
        return new SeededFamily(family.Id, job.Id);
    }

    private sealed record SeededFamily(int FamilyId, int JobId);
}
