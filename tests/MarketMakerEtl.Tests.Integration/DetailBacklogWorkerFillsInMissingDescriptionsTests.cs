using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using MarketMakerEtl.Etl.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class DetailBacklogWorkerFillsInMissingDescriptionsTests
{
    private const string SucceedingUrl = "https://www.mercari.com/us/item/m00000000001/";
    private const string FailingUrl = "https://www.mercari.com/us/item/m00000000002/";

    private const string MercariItemPage = """
        <html>
        <body>
          <h1 data-testid="ItemName">Vintage Camera</h1>
          <div data-testid="ItemPrice">$45</div>
          <div data-testid="ItemDetailsDescription">Barely used, comes with original box.</div>
        </body>
        </html>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-detail-backlog-{Guid.NewGuid():N}.db");
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
    public async Task Should_fill_in_descriptions_and_increment_attempts_on_a_failing_listing_after_one_run_once()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobs = new JobStore(factory);
        var job = await jobs.CreateJob(
            new JobDetails("vintage camera", Marketplace.Mercari, null, 24, true, []), CancellationToken.None);
        var failingListingId = await SeedListing(
            factory, job.Id, "m00000000002", FailingUrl, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var succeedingListingId = await SeedListing(
            factory, job.Id, "m00000000001", SucceedingUrl, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var worker = BuildWorker(jobs, factory);

        await worker.RunOnce(CancellationToken.None);

        var succeeding = await GetListing(factory, succeedingListingId);
        var failing = await GetListing(factory, failingListingId);
        Assert.Multiple(() =>
        {
            Assert.That(succeeding.Description, Is.EqualTo("Barely used, comes with original box."));
            Assert.That(succeeding.DetailFetchedUtc, Is.Not.Null);
            Assert.That(failing.DetailFetchAttempts, Is.EqualTo(1));
            Assert.That(failing.DetailFetchedUtc, Is.Null);
        });
    }

    private static DetailBacklogWorker BuildWorker(JobStore jobs, IDbContextFactory<EtlDbContext> factory)
    {
        var detailStore = new ItemDetailStore(factory);
        var client = new TwoOutcomeScrapeClient(SucceedingUrl, MercariItemPage, FailingUrl);
        var detailFetchOptions = new DetailFetchOptions(MaxConcurrentDetailFetches: 4, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3);
        var detailFetch = new ItemDetailFetchService(
            detailStore, client, [new MercariItemPageParser()], detailFetchOptions, NullLogger<ItemDetailFetchService>.Instance);
        var backlogOptions = new DetailBacklogOptions(
            Enabled: true, TickMinutes: 5, MaxFetchesPerTick: 10, MaxFetchesPerHour: 10, MaxDetailFetchAttempts: 3);
        var backlogService = new DetailBacklogService(jobs, detailStore, detailFetch, backlogOptions, TimeProvider.System);
        return new DetailBacklogWorker(backlogService, backlogOptions, TimeProvider.System, NullLogger<DetailBacklogWorker>.Instance);
    }

    private static async Task<int> SeedListing(
        IDbContextFactory<EtlDbContext> factory, int jobId, string listingId, string url, DateTime createdUtc)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = url,
            ItemStatus = "Active",
            CreatedUtc = createdUtc
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private static async Task<ListingEntity> GetListing(IDbContextFactory<EtlDbContext> factory, int listingEntityId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Listings.SingleAsync(l => l.Id == listingEntityId);
    }

    private sealed class TwoOutcomeScrapeClient(string succeedingUrl, string succeedingHtml, string failingUrl) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            url == succeedingUrl
                ? Task.FromResult(succeedingHtml)
                : url == failingUrl
                    ? throw new InvalidOperationException("item page blocked")
                    : throw new InvalidOperationException($"Unexpected url {url}");
    }
}
