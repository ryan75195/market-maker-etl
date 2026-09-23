using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class EffectivelyEnabledJobsQueryTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-effectively-enabled-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
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
    public async Task Should_exclude_disabled_jobs_and_jobs_whose_categories_are_all_disabled()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();

        var disabledCategory = new CategoryEntity { Name = "Disabled", IsEnabled = false, CreatedUtc = DateTime.UtcNow };
        var enabledCategory = new CategoryEntity { Name = "Enabled", IsEnabled = true, CreatedUtc = DateTime.UtcNow };
        db.Categories.AddRange(disabledCategory, enabledCategory);

        var disabledJob = BuildJob("disabled job", isEnabled: false);
        var allCategoriesDisabledJob = BuildJob("all categories disabled job");
        var mixedCategoriesJob = BuildJob("mixed categories job");
        var noCategoriesJob = BuildJob("no categories job");
        db.ScrapeJobs.AddRange(disabledJob, allCategoriesDisabledJob, mixedCategoriesJob, noCategoriesJob);
        await db.SaveChangesAsync();

        db.JobCategories.AddRange(
            new JobCategoryEntity { ScrapeJobId = allCategoriesDisabledJob.Id, CategoryId = disabledCategory.Id },
            new JobCategoryEntity { ScrapeJobId = mixedCategoriesJob.Id, CategoryId = disabledCategory.Id },
            new JobCategoryEntity { ScrapeJobId = mixedCategoriesJob.Id, CategoryId = enabledCategory.Id });
        await db.SaveChangesAsync();

        var effectivelyEnabled = await db.ScrapeJobs.WhereEffectivelyEnabled().ToListAsync();

        Assert.That(
            effectivelyEnabled.Select(j => j.SearchTerm),
            Is.EquivalentTo(new[] { "mixed categories job", "no categories job" }));
    }

    private static ScrapeJobEntity BuildJob(string searchTerm, bool isEnabled = true) =>
        new() { SearchTerm = searchTerm, IsEnabled = isEnabled, CreatedUtc = DateTime.UtcNow };
}
