using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Families;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ActivateExistingFamiliesMigrationTests
{
    private const string PreOnboardingMigration = "20260927125001_AddClassificationBatchRuns";
    private const string PreExistingFamilyKey = "pre-existing-family";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-family-activation-migration-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
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
    public async Task Should_activate_a_family_that_existed_before_onboarding_was_introduced()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreOnboardingFamilyAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var family = await db.ProductFamilies.SingleAsync(f => f.Key == PreExistingFamilyKey);

        Assert.That(family.State, Is.EqualTo(FamilyState.Active));
    }

    [Test]
    public async Task Should_leave_an_already_hotfixed_family_active()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260927140012_AddFamilyOnboarding");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ProductFamilies (Key, Name, ModelName, CreatedUtc, DealMinDiscount, DealMinSold, State) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                PreExistingFamilyKey,
                "Pre-existing family",
                PreExistingFamilyKey,
                DateTime.UtcNow,
                0.20m,
                5,
                1);
        }

        await factory.ApplyMigrations(CancellationToken.None);

        await using var readDb = await factory.CreateDbContextAsync();
        var family = await readDb.ProductFamilies.SingleAsync(f => f.Key == PreExistingFamilyKey);

        Assert.That(family.State, Is.EqualTo(FamilyState.Active));
    }

    [Test]
    public async Task Should_leave_a_family_created_through_onboarding_as_draft()
    {
        const string OnboardedFamilyKey = "onboarded-family";
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        int familyId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260927140012_AddFamilyOnboarding");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ProductFamilies (Key, Name, ModelName, CreatedUtc, DealMinDiscount, DealMinSold) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                OnboardedFamilyKey,
                "Onboarded family",
                OnboardedFamilyKey,
                DateTime.UtcNow,
                0.20m,
                5);
            familyId = (await db.Database.SqlQueryRaw<int>(
                "SELECT Id FROM ProductFamilies WHERE Key = {0}", OnboardedFamilyKey).ToListAsync()).Single();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO FamilyOnboardings (ProductFamilyId, JobId, SearchTerm, SampleListingsJson, PromptTokens, CompletionTokens, CostUsd, UpdatedUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
                familyId,
                1,
                "onboarded search",
                "[]",
                0,
                0,
                0m,
                DateTime.UtcNow);
        }

        await factory.ApplyMigrations(CancellationToken.None);

        await using var readDb = await factory.CreateDbContextAsync();
        var family = await readDb.ProductFamilies.SingleAsync(f => f.Key == OnboardedFamilyKey);

        Assert.That(family.State, Is.EqualTo(FamilyState.Draft));
    }

    private static async Task SeedPreOnboardingFamilyAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreOnboardingMigration);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ProductFamilies (Key, Name, ModelName, CreatedUtc, DealMinDiscount, DealMinSold) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            PreExistingFamilyKey,
            "Pre-existing family",
            PreExistingFamilyKey,
            DateTime.UtcNow,
            0.20m,
            5);
    }
}
