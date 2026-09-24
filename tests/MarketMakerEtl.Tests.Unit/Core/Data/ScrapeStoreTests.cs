using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
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
        var runId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);

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
        var original = new ListingSummary("111111111111", "PS5", 100m, "GBP", "https://x/itm/1", false, null, null, null);
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
        var runId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);
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
        var runId = await store.EnqueueRun(jobId, "ps5", TriggerType.Manual, CancellationToken.None);
        await store.ClaimNextQueuedRun(CancellationToken.None);
        var searchCompletedUtc = DateTime.UtcNow;
        var detailCompletedUtc = DateTime.UtcNow;

        await store.CompleteRun(
            runId,
            new RunCompletionCounts(1, 0, 0, 0, 0, 1, 1, searchCompletedUtc, detailCompletedUtc),
            CancellationToken.None);

        var run = await store.GetRun(runId, CancellationToken.None);
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var job = await db.ScrapeJobs.SingleAsync(j => j.Id == jobId);

        Assert.Multiple(() =>
        {
            Assert.That(run!.Status, Is.EqualTo(ScrapeRunStatus.Completed));
            Assert.That(run.ListingsAddedActive, Is.EqualTo(1));
            Assert.That(run.TotalReportedBySearch, Is.EqualTo(1));
            Assert.That(job.LastRunUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task Should_deduplicate_listings_within_a_single_upsert()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var first = new ListingSummary("111111111111", "PS5", 100m, "GBP", "https://x/itm/1", false, null, null, null);
        var second = first with { Price = 90m };

        await store.UpsertListings(jobId, [first, second], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(1));
            Assert.That(listings[0].Price, Is.EqualTo(90m));
        });
    }

    [Test]
    public async Task Should_return_active_listings_for_refresh()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Listings.AddRange(
                new ListingEntity
                {
                    ListingId = "refresh-active-open",
                    Url = "https://x/itm/open",
                    ItemStatus = null,
                    CreatedUtc = DateTime.UtcNow
                },
                new ListingEntity
                {
                    ListingId = "refresh-active-labelled",
                    Url = "https://x/itm/labelled",
                    ItemStatus = "Active",
                    CreatedUtc = DateTime.UtcNow
                },
                new ListingEntity
                {
                    ListingId = "refresh-terminal-sold",
                    Url = "https://x/itm/sold",
                    ItemStatus = "Sold",
                    CreatedUtc = DateTime.UtcNow
                },
                new ListingEntity
                {
                    ListingId = "refresh-terminal-ended",
                    Url = "https://x/itm/ended",
                    ItemStatus = "Ended",
                    CreatedUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var targets = await store.GetActiveListings(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                targets.Select(t => t.ListingId),
                Is.EquivalentTo(new[] { "refresh-active-open", "refresh-active-labelled" }));
            Assert.That(
                targets.Single(t => t.ListingId == "refresh-active-open").Url,
                Is.EqualTo("https://x/itm/open"));
        });
    }

    [Test]
    public async Task Should_record_a_status_change_against_a_listing()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = 0;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = new ListingEntity
            {
                ListingId = "refresh-becomes-sold",
                Url = "https://x/itm/becomes-sold",
                ItemStatus = "Active",
                Price = 100m,
                CreatedUtc = DateTime.UtcNow
            };
            db.Listings.Add(listing);
            await db.SaveChangesAsync();
            listingId = listing.Id;
        }

        await store.RecordStatusChange(
            listingId,
            new ListingStatusObservation("Sold", 275.50m, null, null, null, false),
            CancellationToken.None);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = await db.Listings.SingleAsync(l => l.Id == listingId);
            var changes = await db.ListingStatusChanges
                .Where(c => c.ListingEntityId == listingId)
                .ToListAsync();

            Assert.Multiple(() =>
            {
                Assert.That(listing.ItemStatus, Is.EqualTo("Sold"));
                Assert.That(listing.Price, Is.EqualTo(275.50m));
                Assert.That(changes, Has.Count.EqualTo(1));
                Assert.That(changes[0].Status, Is.EqualTo("Sold"));
            });
        }
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
