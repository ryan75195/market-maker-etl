using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class DetailBacklogStoreTests
{
    private const int MaxAttempts = 3;

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

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-detail-backlog-store-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        _jobs = new JobStore(Factory());
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
    public async Task Should_stop_reselecting_a_sold_listing_without_a_date_once_attempts_reach_the_maximum()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "family-sold-no-date-cap", detailFetched: false, isSold: true);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);
            Assert.That(targets.Select(t => t.Id), Does.Contain(listingEntityId));
            await MarkDetailFetchFailed(listingEntityId);
        }

        var finalTargets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);
        var listing = await GetListing(listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.DetailFetchAttempts, Is.EqualTo(MaxAttempts));
            Assert.That(listing.SoldDate, Is.Null);
            Assert.That(finalTargets.Select(t => t.Id), Does.Not.Contain(listingEntityId));
        });
    }

    [Test]
    public async Task Should_drop_a_sold_listing_out_of_the_family_backlog_immediately_once_it_gets_a_real_sold_date()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(
            jobId, "family-sold-with-date-drop", detailFetched: true, isSold: true,
            soldDate: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Is.Empty);
    }

    [Test]
    public async Task Should_order_backlog_listings_sold_first_then_newest_active_then_retries()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var otherJobId = await SeedJob();
        var retry = await SeedListing(jobId, "backlog-retry", detailFetched: false, detailFetchAttempts: 1);
        var oldActive = await SeedListing(
            otherJobId, "backlog-active-old", detailFetched: false,
            postedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newActive = await SeedListing(
            jobId, "backlog-active-new", detailFetched: false,
            postedUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var sold = await SeedListing(otherJobId, "backlog-sold", detailFetched: false, isSold: true);

        var targets = await store.GetBacklogListingsNeedingDetail([jobId, otherJobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(
            targets.Select(t => t.Id),
            Is.EqualTo(new[] { sold, newActive, oldActive, retry }));
    }

    [Test]
    public async Task Should_fall_back_to_created_date_when_posted_date_is_missing_for_active_backlog_listings()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var noPostedDate = await SeedListing(
            jobId, "backlog-no-posted", detailFetched: false,
            createdUtc: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var newerNoPostedDate = await SeedListing(
            jobId, "backlog-newer-no-posted", detailFetched: false,
            createdUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var targets = await store.GetBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { newerNoPostedDate, noPostedDate }));
    }

    [Test]
    public async Task Should_exclude_backlog_listings_that_reached_the_maximum_attempt_count()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "backlog-exhausted", detailFetched: false, detailFetchAttempts: MaxAttempts);
        var eligible = await SeedListing(jobId, "backlog-eligible", detailFetched: false, detailFetchAttempts: MaxAttempts - 1);

        var targets = await store.GetBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { eligible }));
    }

    [Test]
    public async Task Should_cap_backlog_listings_across_jobs_to_the_requested_limit()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var otherJobId = await SeedJob();
        await SeedListing(jobId, "backlog-cap-1", detailFetched: false);
        await SeedListing(otherJobId, "backlog-cap-2", detailFetched: false);
        await SeedListing(jobId, "backlog-cap-3", detailFetched: false);

        var targets = await store.GetBacklogListingsNeedingDetail([jobId, otherJobId], 2, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Should_return_no_backlog_listings_when_no_job_ids_are_eligible()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "backlog-ineligible", detailFetched: false);

        var targets = await store.GetBacklogListingsNeedingDetail([], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Is.Empty);
    }

    [Test]
    public async Task Should_order_the_family_backlog_sold_without_a_date_before_never_fetched()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var neverFetched = await SeedListing(jobId, "family-never-fetched", detailFetched: false);
        var soldWithoutDate = await SeedListing(
            jobId, "family-sold-no-date", detailFetched: false, isSold: true, soldDate: null);

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { soldWithoutDate, neverFetched }));
    }

    [Test]
    public async Task Should_include_an_already_fetched_family_listing_that_is_sold_without_a_real_date()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var requeued = await SeedListing(
            jobId, "family-requeued", detailFetched: true, isSold: true, soldDate: null);

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { requeued }));
    }

    [Test]
    public async Task Should_exclude_a_family_listing_that_already_has_details_and_a_real_sold_date()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(
            jobId, "family-already-detailed", detailFetched: true, isSold: true,
            soldDate: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Is.Empty);
    }

    [Test]
    public async Task Should_exclude_family_listings_that_reached_the_maximum_attempt_count()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "family-exhausted", detailFetched: false, detailFetchAttempts: MaxAttempts);
        var eligible = await SeedListing(jobId, "family-eligible", detailFetched: false, detailFetchAttempts: MaxAttempts - 1);

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { eligible }));
    }

    [Test]
    public async Task Should_cap_family_backlog_listings_to_the_requested_limit()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "family-cap-1", detailFetched: false);
        await SeedListing(jobId, "family-cap-2", detailFetched: false);

        var targets = await store.GetFamilyBacklogListingsNeedingDetail([jobId], 1, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_count_family_listings_still_needing_detail()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "family-count-1", detailFetched: false);
        await SeedListing(jobId, "family-count-2", detailFetched: false);
        await SeedListing(jobId, "family-count-done", detailFetched: true, isSold: false);

        var remaining = await store.CountFamilyListingsNeedingDetail([jobId], MaxAttempts, CancellationToken.None);

        Assert.That(remaining, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_order_in_scope_family_listings_sold_without_date_first_and_exclude_out_of_scope_and_unclassified()
    {
        var store = CreateStore();
        var familyJob = await CreateFamilyJob("in-scope-order-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var outOfScopeId = await SeedListing(familyJob.JobId, "in-scope-order-out-of-scope");
        await SeedClassification(outOfScopeId, taxonomyVersionId, "irrelevant");
        var inScopeNeverFetched = await SeedListing(familyJob.JobId, "in-scope-order-never-fetched");
        await SeedClassification(inScopeNeverFetched, taxonomyVersionId, "relevant");
        var inScopeSoldNoDate = await SeedListing(familyJob.JobId, "in-scope-order-sold-no-date", isSold: true);
        await SeedClassification(inScopeSoldNoDate, taxonomyVersionId, "relevant");
        await SeedListing(familyJob.JobId, "in-scope-order-unclassified");

        var targets = await store.GetFamilyInScopeListingsNeedingDetail(
            [familyJob.JobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { inScopeSoldNoDate, inScopeNeverFetched }));
    }

    [Test]
    public async Task Should_return_no_in_scope_family_listings_when_the_family_has_no_taxonomy_version_yet()
    {
        var store = CreateStore();
        var familyJob = await CreateFamilyJob("in-scope-no-taxonomy-job");
        await SeedListing(familyJob.JobId, "in-scope-no-taxonomy-listing");

        var targets = await store.GetFamilyInScopeListingsNeedingDetail(
            [familyJob.JobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets, Is.Empty);
    }

    [Test]
    public async Task Should_return_all_family_listings_as_unclassified_when_the_family_has_no_taxonomy_version_yet()
    {
        var store = CreateStore();
        var familyJob = await CreateFamilyJob("unclassified-no-taxonomy-job");
        var listingId = await SeedListing(familyJob.JobId, "unclassified-no-taxonomy-listing");

        var targets = await store.GetFamilyUnclassifiedListingsNeedingDetail(
            [familyJob.JobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { listingId }));
    }

    [Test]
    public async Task Should_return_only_unclassified_family_listings_excluding_in_scope_and_out_of_scope()
    {
        var store = CreateStore();
        var familyJob = await CreateFamilyJob("unclassified-only-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var inScopeId = await SeedListing(familyJob.JobId, "unclassified-only-in-scope");
        await SeedClassification(inScopeId, taxonomyVersionId, "relevant");
        var outOfScopeId = await SeedListing(familyJob.JobId, "unclassified-only-out-of-scope");
        await SeedClassification(outOfScopeId, taxonomyVersionId, "irrelevant");
        var unclassifiedId = await SeedListing(familyJob.JobId, "unclassified-only-unclassified");

        var targets = await store.GetFamilyUnclassifiedListingsNeedingDetail(
            [familyJob.JobId], 10, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Select(t => t.Id), Is.EqualTo(new[] { unclassifiedId }));
    }

    [Test]
    public async Task Should_count_only_in_scope_family_listings_still_needing_detail()
    {
        var store = CreateStore();
        var familyJob = await CreateFamilyJob("count-in-scope-job");
        var taxonomyVersionId = await SeedTaxonomyVersion(familyJob.FamilyId, SingleGateTaxonomyJson);
        var inScopeOne = await SeedListing(familyJob.JobId, "count-in-scope-1");
        await SeedClassification(inScopeOne, taxonomyVersionId, "relevant");
        var inScopeTwo = await SeedListing(familyJob.JobId, "count-in-scope-2");
        await SeedClassification(inScopeTwo, taxonomyVersionId, "relevant");
        var outOfScope = await SeedListing(familyJob.JobId, "count-out-of-scope");
        await SeedClassification(outOfScope, taxonomyVersionId, "irrelevant");
        await SeedListing(familyJob.JobId, "count-unclassified");

        var remaining = await store.CountFamilyInScopeListingsNeedingDetail(
            [familyJob.JobId], MaxAttempts, CancellationToken.None);

        Assert.That(remaining, Is.EqualTo(2));
    }

    private async Task<int> CreateJob(string searchTerm)
    {
        var job = await _jobs.CreateJob(new JobDetails(searchTerm, Marketplace.Mercari, null, 24, true, []), CancellationToken.None);
        return job.Id;
    }

    private async Task<FamilyJob> CreateFamilyJob(string searchTerm)
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

    private async Task SeedClassification(int listingEntityId, int taxonomyVersionId, string resolvedChoice)
    {
        await using var db = await Factory().CreateDbContextAsync();
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingEntityId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = "item_type",
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

    private async Task<int> SeedJob() => await CreateJob($"job-{Guid.NewGuid():N}");

    private async Task<int> SeedListing(
        int jobId,
        string listingId,
        bool detailFetched = false,
        bool isSold = false,
        int detailFetchAttempts = 0,
        DateTime? postedUtc = null,
        DateTime? createdUtc = null,
        DateTime? soldDate = null)
    {
        await using var db = await Factory().CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Url = $"https://x/itm/{listingId}",
            ItemStatus = isSold ? "Sold" : "Active",
            IsSold = isSold,
            SoldDate = soldDate,
            DetailFetchedUtc = detailFetched ? DateTime.UtcNow : null,
            DetailFetchAttempts = detailFetchAttempts,
            PostedUtc = postedUtc,
            CreatedUtc = createdUtc ?? DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private async Task<ListingEntity> GetListing(int listingEntityId)
    {
        await using var db = await Factory().CreateDbContextAsync();
        return await db.Listings.SingleAsync(l => l.Id == listingEntityId);
    }

    private async Task MarkDetailFetchFailed(int listingEntityId)
    {
        var store = new ItemDetailStore(Factory());
        await store.MarkDetailFetchFailed(listingEntityId, MaxAttempts, CancellationToken.None);
    }

    private IDbContextFactory<EtlDbContext> Factory() => _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

    private DetailBacklogStore CreateStore() => new(Factory());

    private sealed record FamilyJob(int JobId, int FamilyId);
}
