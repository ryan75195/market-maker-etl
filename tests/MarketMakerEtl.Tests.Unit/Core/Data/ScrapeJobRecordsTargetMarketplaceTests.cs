using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeJobRecordsTargetMarketplaceTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-job-marketplace-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_the_marketplace_a_scrape_job_targets()
    {
        var store = CreateStore();

        var jobId = await store.EnsureJob("mercari-target", CancellationToken.None, Marketplace.Mercari);

        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var job = await db.ScrapeJobs.SingleAsync(j => j.Id == jobId);

        Assert.That(job.Marketplace, Is.EqualTo(Marketplace.Mercari));
    }

    [Test]
    public async Task Should_treat_a_scrape_job_without_a_marketplace_as_ebay()
    {
        var store = CreateStore();

        var jobId = await store.EnsureJob("ebay-target", CancellationToken.None);

        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var job = await db.ScrapeJobs.SingleAsync(j => j.Id == jobId);

        Assert.That(job.Marketplace, Is.EqualTo(Marketplace.Ebay));
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
