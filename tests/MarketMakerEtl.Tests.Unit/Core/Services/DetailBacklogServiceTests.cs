using Microsoft.Extensions.Time.Testing;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DetailBacklogServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private JobStore _jobs = null!;
    private ItemDetailStore _detailStore = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-detail-backlog-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        _jobs = new JobStore(Factory());
        _detailStore = new ItemDetailStore(Factory());
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
    public async Task Should_do_nothing_when_the_backlog_worker_is_disabled()
    {
        var jobId = await CreateJob("disabled-job");
        await SeedListing(jobId, "disabled-listing");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options(enabled: false));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(0));
            Assert.That(fetch.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task Should_do_nothing_when_the_hourly_budget_is_exhausted()
    {
        var jobId = await CreateJob("no-budget-job");
        await SeedListing(jobId, "no-budget-listing");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerHour: 0));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(0));
            Assert.That(fetch.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task Should_skip_a_job_that_has_a_queued_or_running_run()
    {
        var busyJobId = await CreateJob("busy-job");
        await EnqueueRun(busyJobId);
        await SeedListing(busyJobId, "busy-listing");
        var freeJobId = await CreateJob("free-job");
        await SeedListing(freeJobId, "free-listing");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options());

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Is.EqualTo(new[] { "free-listing" }));
    }

    [Test]
    public async Task Should_select_listings_across_all_eligible_jobs_up_to_the_tick_cap()
    {
        var jobOne = await CreateJob("cap-job-1");
        var jobTwo = await CreateJob("cap-job-2");
        await SeedListing(jobOne, "cap-1");
        await SeedListing(jobTwo, "cap-2");
        await SeedListing(jobOne, "cap-3");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 2));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(2));
            Assert.That(fetch.Calls, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_never_select_a_listing_that_reached_the_maximum_attempt_count()
    {
        var jobId = await CreateJob("exhausted-job");
        await SeedListing(jobId, "exhausted-listing", attempts: 3);
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options(maxAttempts: 3));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(0));
            Assert.That(fetch.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task Should_stop_the_tick_early_when_every_attempted_fetch_fails()
    {
        var jobId = await CreateJob("outage-job");
        await SeedListing(jobId, "outage-1");
        await SeedListing(jobId, "outage-2");
        await SeedListing(jobId, "outage-3");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => false);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options());

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(3));
            Assert.That(result.Attempted, Is.EqualTo(1));
            Assert.That(result.Succeeded, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(fetch.Calls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_keep_attempting_later_listings_once_a_fetch_has_succeeded_in_the_tick()
    {
        var jobId = await CreateJob("mixed-job");
        await SeedListing(jobId, "mixed-sold", isSold: true);
        await SeedListing(jobId, "mixed-active-1");
        await SeedListing(jobId, "mixed-active-2");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, target => target.ListingId == "mixed-sold");
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options());

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Attempted, Is.EqualTo(3));
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(result.Failures, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_enforce_the_hourly_budget_across_ticks_and_reset_after_an_hour()
    {
        var jobId = await CreateJob("budget-job");
        await SeedListing(jobId, "budget-1");
        await SeedListing(jobId, "budget-2");
        await SeedListing(jobId, "budget-3");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var timeProvider = new FakeTimeProvider(StartTime);
        var service = CreateService(fetch, timeProvider, Options(maxFetchesPerTick: 30, maxFetchesPerHour: 2));

        var firstTick = await service.RunTick(CancellationToken.None);
        var secondTick = await service.RunTick(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(1));
        var thirdTick = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstTick.Attempted, Is.EqualTo(2));
            Assert.That(secondTick.Selected, Is.EqualTo(0));
            Assert.That(thirdTick.Attempted, Is.EqualTo(1));
        });
    }

    private DetailBacklogService CreateService(
        RecordingDetailFetchService fetch, TimeProvider timeProvider, DetailBacklogOptions options) =>
        new(_jobs, _detailStore, fetch, options, timeProvider);

    private static DetailBacklogOptions Options(
        bool enabled = true,
        int tickMinutes = 5,
        int maxFetchesPerTick = 30,
        int maxFetchesPerHour = 300,
        int maxAttempts = 3) =>
        new(enabled, tickMinutes, maxFetchesPerTick, maxFetchesPerHour, maxAttempts);

    private async Task<int> CreateJob(string searchTerm)
    {
        var job = await _jobs.CreateJob(new JobDetails(searchTerm, Marketplace.Mercari, null, 24, true, []), CancellationToken.None);
        return job.Id;
    }

    private async Task EnqueueRun(int jobId)
    {
        var scrapeStore = new ScrapeStore(Factory(), new ScrapeRunStateService());
        await scrapeStore.EnqueueRun(jobId, "term", TriggerType.Manual, CancellationToken.None);
    }

    private async Task SeedListing(int jobId, string listingId, int attempts = 0, bool isSold = false)
    {
        await using var db = await Factory().CreateDbContextAsync();
        db.Listings.Add(new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Url = $"https://x/itm/{listingId}",
            ItemStatus = isSold ? "Sold" : "Active",
            IsSold = isSold,
            DetailFetchAttempts = attempts,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private IDbContextFactory<EtlDbContext> Factory() => _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

    private sealed class RecordingDetailFetchService : IItemDetailFetchService
    {
        private static readonly ItemPageListing MinimalDetail =
            new(null, null, null, null, null, null, null, null, null, null, null);

        private readonly IItemDetailStore _store;
        private readonly int _maxAttempts;
        private readonly Func<ListingDetailTarget, bool> _shouldSucceed;

        public RecordingDetailFetchService(IItemDetailStore store, int maxAttempts, Func<ListingDetailTarget, bool> shouldSucceed)
        {
            _store = store;
            _maxAttempts = maxAttempts;
            _shouldSucceed = shouldSucceed;
        }

        public List<ListingDetailTarget> Calls { get; } = [];

        public Task<IReadOnlyList<ScrapeRunIssueDetails>> FetchDetails(int jobId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<ScrapeRunIssueDetails?> FetchListingDetail(ListingDetailTarget target, CancellationToken ct)
        {
            Calls.Add(target);

            if (_shouldSucceed(target))
            {
                await _store.ApplyItemDetail(target.Id, MinimalDetail, ct);
                return null;
            }

            await _store.MarkDetailFetchFailed(target.Id, _maxAttempts, ct);
            return new ScrapeRunIssueDetails(target.ListingId, "ItemDetailFetchFailed", "boom", "Detail", null);
        }

        public Task ApplyBackfilledDetails(
            int jobId, IReadOnlyDictionary<string, ItemPageListing> detailsByListingId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
