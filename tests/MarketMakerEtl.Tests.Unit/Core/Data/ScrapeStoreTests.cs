using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-{Guid.NewGuid():N}.db");
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
    public async Task Should_enqueue_and_claim_a_queued_run()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, "ps5", CancellationToken.None);

        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(work!.RunId, Is.EqualTo(runId));
            Assert.That(work.SearchTerm, Is.EqualTo("ps5"));
        });
    }

    [Test]
    public async Task Should_upsert_listings_by_listing_id()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var original = new ListingSummary("111111111111", "PS5", 100m, "GBP", "https://x/itm/1", false);
        await store.UpsertListings(jobId, [original], CancellationToken.None);

        await store.UpsertListings(jobId, [original with { Price = 90m }], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(1));
            Assert.That(listings[0].Price, Is.EqualTo(90m));
        });
    }

    [Test]
    public async Task Should_record_a_failed_run_with_its_cause()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, "ps5", CancellationToken.None);
        await store.ClaimNextQueuedRun(CancellationToken.None);

        await store.FailRun(runId, "scraper unreachable", CancellationToken.None);

        var run = await store.GetRun(runId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(run!.Status, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(run.ErrorMessage, Is.EqualTo("scraper unreachable"));
        });
    }

    [Test]
    public async Task Should_record_a_completed_run()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, "ps5", CancellationToken.None);
        await store.ClaimNextQueuedRun(CancellationToken.None);

        await store.CompleteRun(runId, CancellationToken.None);

        var run = await store.GetRun(runId, CancellationToken.None);
        Assert.That(run!.Status, Is.EqualTo(ScrapeRunStatus.Completed));
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
