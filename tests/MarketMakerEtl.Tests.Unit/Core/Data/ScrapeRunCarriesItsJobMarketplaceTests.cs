using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeRunCarriesItsJobMarketplaceTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-run-marketplace-{Guid.NewGuid():N}.db");
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
    public async Task Should_carry_the_marketplace_of_its_job_onto_the_scrape_run()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedMercariJob(factory);

        var runId = await store.EnqueueRun(jobId, "mercari-run", TriggerType.Manual, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScrapeRuns.SingleAsync(r => r.Id == runId);

        Assert.Multiple(() =>
        {
            Assert.That(run.Marketplace, Is.EqualTo(Marketplace.Mercari));
            Assert.That(work!.Marketplace, Is.EqualTo(Marketplace.Mercari));
        });
    }

    private static async Task<int> SeedMercariJob(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var job = new ScrapeJobEntity
        {
            SearchTerm = "mercari-run",
            Marketplace = Marketplace.Mercari,
            CreatedUtc = DateTime.UtcNow
        };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
