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

    private const string SingleGateTaxonomyJson =
        """
        {
         "family": "test-family",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What kind of item is this?",
           "criteria": {
            "relevant": "A relevant item for this family.",
            "irrelevant": "Not relevant to this family."
           }
          },
          "attribute": {
           "instructions": "Which attribute does this item have?",
           "criteria": {
            "a": "Attribute A.",
            "b": "Attribute B.",
            "not_stated": "Not stated."
           },
           "askWhen": [
            {
             "question": "item_type",
             "anyOf": [
              "relevant"
             ]
            }
           ]
          }
         }
        }
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private JobStore _jobs = null!;
    private ItemDetailStore _detailStore = null!;
    private DetailBacklogStore _backlog = null!;

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
        _backlog = new DetailBacklogStore(Factory());
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
    public async Task Should_run_up_to_the_configured_number_of_concurrent_fetches()
    {
        var jobId = await CreateJob("concurrency-job");
        for (var i = 0; i < 6; i++)
        {
            await SeedListing(jobId, $"concurrency-{i}");
        }

        var fetch = new ConcurrencyTrackingDetailFetchService(TimeSpan.FromMilliseconds(50), _ => true);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 6, concurrency: 3));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.EqualTo(6));
            Assert.That(fetch.MaxObservedConcurrency, Is.GreaterThan(1));
            Assert.That(fetch.MaxObservedConcurrency, Is.LessThanOrEqualTo(3));
        });
    }

    [Test]
    public async Task Should_stop_starting_new_fetches_after_one_full_batch_of_failures_under_concurrency()
    {
        var jobId = await CreateJob("outage-concurrent-job");
        for (var i = 0; i < 12; i++)
        {
            await SeedListing(jobId, $"outage-concurrent-{i}");
        }

        var fetch = new ConcurrencyTrackingDetailFetchService(TimeSpan.FromMilliseconds(10), _ => false);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 12, concurrency: 4));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Selected, Is.EqualTo(12));
            Assert.That(result.Attempted, Is.EqualTo(4));
            Assert.That(result.Succeeded, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(4));
            Assert.That(fetch.Calls, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task Should_never_exceed_the_hourly_budget_even_with_concurrent_fetches()
    {
        var jobId = await CreateJob("budget-concurrent-job");
        for (var i = 0; i < 8; i++)
        {
            await SeedListing(jobId, $"budget-concurrent-{i}");
        }

        var fetch = new ConcurrencyTrackingDetailFetchService(TimeSpan.FromMilliseconds(10), _ => true);
        var service = CreateService(
            fetch,
            new FakeTimeProvider(StartTime),
            Options(maxFetchesPerTick: 10, maxFetchesPerHour: 5, concurrency: 4));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Attempted, Is.EqualTo(5));
            Assert.That(fetch.Calls, Has.Count.EqualTo(5));
            Assert.That(fetch.MaxObservedConcurrency, Is.LessThanOrEqualTo(4));
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

    [Test]
    public async Task Should_honour_the_family_detail_budget_per_tick()
    {
        var familyJobId = await CreateFamilyJob("family-budget-job");
        await SeedListing(familyJobId, "family-budget-1");
        await SeedListing(familyJobId, "family-budget-2");
        await SeedListing(familyJobId, "family-budget-3");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 0, familyFetchesPerTick: 1));

        var result = await service.RunTick(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetch.Calls, Has.Count.EqualTo(1));
            Assert.That(result.FamilyAttempted, Is.EqualTo(1));
            Assert.That(result.FamilyRemaining, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_prioritise_family_sold_without_date_listings_over_never_fetched_ones()
    {
        var familyJobId = await CreateFamilyJob("family-priority-job");
        await SeedListing(familyJobId, "family-priority-active");
        await SeedListing(familyJobId, "family-priority-sold", isSold: true);
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 0, familyFetchesPerTick: 1));

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Is.EqualTo(new[] { "family-priority-sold" }));
    }

    [Test]
    public async Task Should_requeue_an_already_fetched_family_listing_that_became_sold_but_leave_non_family_jobs_alone()
    {
        var familyJobId = await CreateFamilyJob("family-requeue-job");
        var nonFamilyJobId = await CreateJob("non-family-requeue-job");
        await SeedListing(familyJobId, "family-requeue", isSold: true, detailFetched: true);
        await SeedListing(nonFamilyJobId, "non-family-requeue", isSold: true, detailFetched: true);
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options());

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Is.EqualTo(new[] { "family-requeue" }));
    }

    [Test]
    public async Task Should_requeue_a_family_listing_after_a_real_active_to_sold_transition_via_upsert()
    {
        var familyJobId = await CreateFamilyJob("family-transition-job");
        await SeedListing(familyJobId, "family-transition", isSold: false, detailFetched: true);
        var scrapeStore = new ScrapeStore(Factory(), new ScrapeRunStateService());
        var soldSummary = new ListingSummary(
            "family-transition", "Title", 50m, "USD", "https://x/itm/family-transition", true, null, null, null);
        await scrapeStore.UpsertListings(familyJobId, [soldSummary], CancellationToken.None);
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(fetch, new FakeTimeProvider(StartTime), Options());

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Is.EqualTo(new[] { "family-transition" }));
    }

    [Test]
    public async Task Should_fetch_in_scope_family_listings_before_unclassified_ones_and_never_fetch_out_of_scope_listings()
    {
        var familyJob = await CreateFamilyJobWithId("family-scope-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var outOfScopeId = await SeedListing(familyJob.JobId, "family-scope-out-of-scope");
        await SeedClassification(outOfScopeId, taxonomyVersionId, "item_type", "irrelevant");
        var inScopeId = await SeedListing(familyJob.JobId, "family-scope-in-scope");
        await SeedClassification(inScopeId, taxonomyVersionId, "item_type", "relevant");
        await SeedListing(familyJob.JobId, "family-scope-unclassified");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 0, familyFetchesPerTick: 10));

        await service.RunTick(CancellationToken.None);

        Assert.That(
            fetch.Calls.Select(t => t.ListingId),
            Is.EqualTo(new[] { "family-scope-in-scope", "family-scope-unclassified" }));
    }

    [Test]
    public async Task Should_still_fetch_out_of_scope_family_listings_through_the_general_pass()
    {
        var familyJob = await CreateFamilyJobWithId("family-scope-general-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var outOfScopeId = await SeedListing(familyJob.JobId, "family-general-out-of-scope");
        await SeedClassification(outOfScopeId, taxonomyVersionId, "item_type", "irrelevant");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(
            fetch, new FakeTimeProvider(StartTime), Options(maxFetchesPerTick: 10, familyFetchesPerTick: 0));

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Does.Contain("family-general-out-of-scope"));
    }

    [Test]
    public async Task Should_ignore_classification_scope_in_the_family_pass_when_in_scope_only_is_disabled()
    {
        var familyJob = await CreateFamilyJobWithId("family-scope-disabled-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var outOfScopeId = await SeedListing(familyJob.JobId, "family-disabled-out-of-scope");
        await SeedClassification(outOfScopeId, taxonomyVersionId, "item_type", "irrelevant");
        var fetch = new RecordingDetailFetchService(_detailStore, 3, _ => true);
        var service = CreateService(
            fetch,
            new FakeTimeProvider(StartTime),
            Options(maxFetchesPerTick: 0, familyFetchesPerTick: 10, familyInScopeOnly: false));

        await service.RunTick(CancellationToken.None);

        Assert.That(fetch.Calls.Select(t => t.ListingId), Does.Contain("family-disabled-out-of-scope"));
    }

    private DetailBacklogService CreateService(
        IItemDetailFetchService fetch, TimeProvider timeProvider, DetailBacklogOptions options) =>
        new(_jobs, _backlog, fetch, options, timeProvider);

    private static DetailBacklogOptions Options(
        bool enabled = true,
        int tickMinutes = 5,
        int maxFetchesPerTick = 30,
        int maxFetchesPerHour = 300,
        int maxAttempts = 3,
        int familyFetchesPerTick = 300,
        int concurrency = 1,
        bool familyInScopeOnly = true) =>
        new(
            enabled,
            tickMinutes,
            maxFetchesPerTick,
            maxFetchesPerHour,
            maxAttempts,
            familyFetchesPerTick,
            concurrency,
            familyInScopeOnly);

    private async Task<int> CreateJob(string searchTerm)
    {
        var job = await _jobs.CreateJob(new JobDetails(searchTerm, Marketplace.Mercari, null, 24, true, []), CancellationToken.None);
        return job.Id;
    }

    private async Task<int> CreateFamilyJob(string searchTerm)
    {
        var familyJob = await CreateFamilyJobWithId(searchTerm);
        return familyJob.JobId;
    }

    private async Task<FamilyJob> CreateFamilyJobWithId(string searchTerm)
    {
        var jobId = await CreateJob(searchTerm);
        var families = new ProductFamilyStore(Factory());
        var family = await families.CreateFamily($"family-{Guid.NewGuid():N}", "Test Family", "test-model", CancellationToken.None);
        await families.SetJobFamily(jobId, family!.Id, CancellationToken.None);
        return new FamilyJob(jobId, family.Id);
    }

    private async Task<int> SeedTaxonomyVersion(int familyId, string questionsJson)
    {
        var families = new ProductFamilyStore(Factory());
        var version = await families.AddTaxonomyVersion(familyId, questionsJson, CancellationToken.None);
        return version!.Id;
    }

    private async Task SeedClassification(
        int listingEntityId, int taxonomyVersionId, string question, string resolvedChoice)
    {
        await using var db = await Factory().CreateDbContextAsync();
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingEntityId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = question,
            Choice = resolvedChoice,
            ResolvedChoice = resolvedChoice,
            IsApplicable = true,
            Confidence = 1.0,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task EnqueueRun(int jobId)
    {
        var scrapeStore = new ScrapeStore(Factory(), new ScrapeRunStateService());
        await scrapeStore.EnqueueRun(jobId, "term", TriggerType.Manual, CancellationToken.None);
    }

    private async Task<int> SeedListing(
        int jobId, string listingId, int attempts = 0, bool isSold = false, bool detailFetched = false)
    {
        await using var db = await Factory().CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Url = $"https://x/itm/{listingId}",
            ItemStatus = isSold ? "Sold" : "Active",
            IsSold = isSold,
            DetailFetchedUtc = detailFetched ? DateTime.UtcNow : null,
            DetailFetchAttempts = attempts,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private IDbContextFactory<EtlDbContext> Factory() => _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

    private sealed record FamilyJob(int JobId, int FamilyId);

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

    private sealed class ConcurrencyTrackingDetailFetchService : IItemDetailFetchService
    {
        private readonly TimeSpan _delay;
        private readonly Func<ListingDetailTarget, bool> _shouldSucceed;
        private readonly object _lock = new();
        private int _inFlight;

        public ConcurrencyTrackingDetailFetchService(TimeSpan delay, Func<ListingDetailTarget, bool> shouldSucceed)
        {
            _delay = delay;
            _shouldSucceed = shouldSucceed;
        }

        public List<ListingDetailTarget> Calls { get; } = [];

        public int MaxObservedConcurrency { get; private set; }

        public Task<IReadOnlyList<ScrapeRunIssueDetails>> FetchDetails(int jobId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<ScrapeRunIssueDetails?> FetchListingDetail(ListingDetailTarget target, CancellationToken ct)
        {
            lock (_lock)
            {
                Calls.Add(target);
            }

            var current = Interlocked.Increment(ref _inFlight);
            RecordObservedConcurrency(current);

            try
            {
                await Task.Delay(_delay, ct);
                return _shouldSucceed(target)
                    ? null
                    : new ScrapeRunIssueDetails(target.ListingId, "ItemDetailFetchFailed", "boom", "Detail", null);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task ApplyBackfilledDetails(
            int jobId, IReadOnlyDictionary<string, ItemPageListing> detailsByListingId, CancellationToken ct) =>
            throw new NotSupportedException();

        private void RecordObservedConcurrency(int current)
        {
            lock (_lock)
            {
                if (current > MaxObservedConcurrency)
                {
                    MaxObservedConcurrency = current;
                }
            }
        }
    }
}
