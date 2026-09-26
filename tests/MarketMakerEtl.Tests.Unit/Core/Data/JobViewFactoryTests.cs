using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class JobViewFactoryTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-job-view-factory-{Guid.NewGuid():N}.db");
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
    public async Task Should_include_job_categories_and_their_categories_when_querying()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            var category = new CategoryEntity { Name = "Collectibles", IsEnabled = true, CreatedUtc = DateTime.UtcNow };
            var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
            seedDb.Categories.Add(category);
            seedDb.ScrapeJobs.Add(job);
            await seedDb.SaveChangesAsync();
            seedDb.JobCategories.Add(new JobCategoryEntity { ScrapeJobId = job.Id, CategoryId = category.Id });
            await seedDb.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var loaded = await db.ScrapeJobs.IncludeCategories().SingleAsync();

        Assert.That(loaded.JobCategories.Single().Category!.Name, Is.EqualTo("Collectibles"));
    }

    [Test]
    public void Should_map_a_job_entity_to_a_view_including_its_applicable_categories_and_family()
    {
        var category = new CategoryEntity { Id = 7, Name = "Consoles", IsEnabled = true, CreatedUtc = DateTime.UtcNow };
        var job = new ScrapeJobEntity
        {
            Id = 1,
            SearchTerm = "ps5 controller",
            Marketplace = Marketplace.Mercari,
            FilterInstructions = "genuine only",
            IntervalHours = 12,
            IsEnabled = false,
            CreatedUtc = DateTime.UtcNow,
            ProductFamilyId = 42
        };
        job.JobCategories.Add(new JobCategoryEntity { ScrapeJobId = job.Id, CategoryId = category.Id, Category = category });
        job.JobCategories.Add(new JobCategoryEntity { ScrapeJobId = job.Id, CategoryId = 999, Category = null });

        var view = job.ToView();

        Assert.Multiple(() =>
        {
            Assert.That(view.SearchTerm, Is.EqualTo("ps5 controller"));
            Assert.That(view.Marketplace, Is.EqualTo(Marketplace.Mercari));
            Assert.That(view.IsEnabled, Is.False);
            Assert.That(view.ProductFamilyId, Is.EqualTo(42));
            Assert.That(view.Categories.Select(c => c.Name), Is.EquivalentTo(new[] { "Consoles" }));
        });
    }
}
