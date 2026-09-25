using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ItemDetailStoreTests
{
    private const int MaxAttempts = 3;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-item-detail-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_return_only_listings_without_a_detail_fetch_up_to_the_limit()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "detail-needed-1", detailFetched: false);
        await SeedListing(jobId, "detail-needed-2", detailFetched: false);
        await SeedListing(jobId, "detail-already-fetched", detailFetched: true);

        var targets = await store.GetListingsNeedingDetail(jobId, 1, MaxAttempts, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].ListingId, Is.EqualTo("detail-needed-1"));
        });
    }

    [Test]
    public async Task Should_apply_description_images_shipping_and_seller_from_an_item_page()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-apply-active", detailFetched: false);
        var detail = BuildItemPageListing(status: null);

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);

        var listing = await GetListing(listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.Description, Is.EqualTo("A great item"));
            Assert.That(listing.DescriptionStatus, Is.EqualTo("ok"));
            Assert.That(listing.Seller, Is.EqualTo("Some Seller"));
            Assert.That(listing.ShippingCost, Is.EqualTo(5.00m));
            Assert.That(listing.OriginalPrice, Is.EqualTo(20.00m));
            Assert.That(listing.Likes, Is.EqualTo(3));
            Assert.That(ListingImageUrlsJson.Deserialize(listing.ImageUrls), Is.EqualTo(new[] { "https://img/1.jpg" }));
            Assert.That(listing.DetailFetchedUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task Should_record_sold_price_sold_date_and_a_status_history_row_for_a_sold_item_page()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-apply-sold", detailFetched: false, isSold: true);
        var detail = BuildItemPageListing(status: "Sold");

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);

        var listing = await GetListing(listingEntityId);
        var historyRows = await GetHistory(listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(listing.SoldPrice, Is.EqualTo(15.00m));
            Assert.That(listing.SoldDate, Is.EqualTo(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(historyRows.Count(r => r.Source == "StatusUpdate"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_increment_attempts_and_stay_eligible_without_marking_failed_on_a_single_failure()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-fetch-retry", detailFetched: false);

        await store.MarkDetailFetchFailed(listingEntityId, MaxAttempts, CancellationToken.None);

        var listing = await GetListing(listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.DetailFetchAttempts, Is.EqualTo(1));
            Assert.That(listing.DetailFetchedUtc, Is.Null);
            Assert.That(listing.DescriptionStatus, Is.Not.EqualTo("failed"));
        });
    }

    [Test]
    public async Task Should_mark_the_listing_failed_once_attempts_reach_the_configured_maximum()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-fetch-exhausted", detailFetched: false);

        await store.MarkDetailFetchFailed(listingEntityId, MaxAttempts, CancellationToken.None);
        await store.MarkDetailFetchFailed(listingEntityId, MaxAttempts, CancellationToken.None);
        await store.MarkDetailFetchFailed(listingEntityId, MaxAttempts, CancellationToken.None);

        var listing = await GetListing(listingEntityId);
        var targets = await store.GetListingsNeedingDetail(jobId, 10, MaxAttempts, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(listing.DetailFetchAttempts, Is.EqualTo(MaxAttempts));
            Assert.That(listing.DescriptionStatus, Is.EqualTo("failed"));
            Assert.That(listing.DetailFetchedUtc, Is.Null);
            Assert.That(targets.Any(t => t.ListingId == "detail-fetch-exhausted"), Is.False);
        });
    }

    [Test]
    public async Task Should_prefer_never_attempted_listings_over_previously_failed_ones_when_the_cap_is_tight()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var previouslyFailedId = await SeedListing(jobId, "detail-previously-failed", detailFetched: false);
        await store.MarkDetailFetchFailed(previouslyFailedId, MaxAttempts, CancellationToken.None);
        await SeedListing(jobId, "detail-never-attempted", detailFetched: false);

        var targets = await store.GetListingsNeedingDetail(jobId, 1, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Single().ListingId, Is.EqualTo("detail-never-attempted"));
    }

    [Test]
    public async Task Should_return_never_attempted_sold_listings_before_others_when_the_cap_is_tight()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        await SeedListing(jobId, "never-attempted-active", detailFetched: false, isSold: false);
        await SeedListing(jobId, "never-attempted-sold", detailFetched: false, isSold: true);

        var targets = await store.GetListingsNeedingDetail(jobId, 1, MaxAttempts, CancellationToken.None);

        Assert.That(targets.Single().ListingId, Is.EqualTo("never-attempted-sold"));
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
    public async Task Should_enrich_the_existing_sold_history_row_instead_of_adding_a_duplicate_when_already_sold()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "already-sold-from-search", detailFetched: false, isSold: true);
        await AddHistoryRow(listingEntityId, "Sold", "InitialScrape", price: null, soldDateUtc: null);
        var detail = BuildItemPageListing(status: "Sold");

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);

        var historyRows = await GetHistory(listingEntityId);
        var soldRows = historyRows.Where(r => r.Status == "Sold").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(soldRows, Has.Count.EqualTo(1));
            Assert.That(soldRows[0].SoldDateUtc, Is.EqualTo(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(soldRows[0].Price, Is.EqualTo(15.00m));
            Assert.That(soldRows[0].Source, Is.EqualTo("InitialScrape"));
        });
    }

    [Test]
    public async Task Should_succeed_when_a_transient_sqlite_lock_blocks_the_save()
    {
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-apply-lock-contended", detailFetched: false);
        var detail = BuildItemPageListing(status: null);

        var lockingServices = new ServiceCollection();
        lockingServices.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath};Default Timeout=1"));
        await using var lockingProvider = lockingServices.BuildServiceProvider();
        var store = new ItemDetailStore(lockingProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

        await using var blocker = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await blocker.OpenAsync();
        await using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync();
        }

        var releaseLock = Task.Run(async () =>
        {
            await Task.Delay(1500);
            await using var rollback = blocker.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync();
        });

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);
        await releaseLock;

        var listing = await GetListing(listingEntityId);
        Assert.That(listing.DetailFetchedUtc, Is.Not.Null);
    }

    [Test]
    public async Task Should_map_listing_ids_to_entity_ids_for_only_the_matching_job_and_requested_ids()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var otherJobId = await SeedJob();
        var matchingId = await SeedListing(jobId, "entity-id-match", detailFetched: false);
        await SeedListing(jobId, "entity-id-not-requested", detailFetched: false);
        await SeedListing(otherJobId, "entity-id-other-job", detailFetched: false);

        var entityIds = await store.GetListingEntityIds(
            jobId, ["entity-id-match", "entity-id-other-job", "missing-id"], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(entityIds, Has.Count.EqualTo(1));
            Assert.That(entityIds["entity-id-match"], Is.EqualTo(matchingId));
        });
    }

    [Test]
    public async Task Should_add_exactly_one_sold_row_when_an_active_listing_is_revealed_sold_by_the_item_page()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "active-revealed-sold", detailFetched: false, isSold: false);
        await AddHistoryRow(listingEntityId, "Active", "InitialScrape", price: 10.00m, soldDateUtc: null);
        var detail = BuildItemPageListing(status: "Sold");

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);

        var historyRows = await GetHistory(listingEntityId);
        var soldRows = historyRows.Where(r => r.Status == "Sold").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(soldRows, Has.Count.EqualTo(1));
            Assert.That(soldRows[0].Source, Is.EqualTo("StatusUpdate"));
            Assert.That(soldRows[0].SoldDateUtc, Is.EqualTo(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    private static ItemPageListing BuildItemPageListing(string? status) =>
        new(
            ListingId: null,
            Title: "Item Title",
            Price: 15.00m,
            Currency: "USD",
            Condition: "Good",
            BuyingFormat: null,
            Status: status,
            SoldPrice: status == "Sold" ? 15.00m : null,
            SoldDate: status == "Sold" ? "2026-09-20T00:00:00Z" : null,
            Seller: "Some Seller",
            PrimaryImageUrl: "https://img/1.jpg",
            Description: "A great item",
            ImageUrls: ["https://img/1.jpg"],
            ShippingCost: 5.00m,
            OriginalPrice: 20.00m,
            Likes: 3);

    private async Task<int> SeedJob()
    {
        await using var db = await Factory().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> SeedListing(
        int jobId,
        string listingId,
        bool detailFetched,
        bool isSold = false,
        int detailFetchAttempts = 0,
        DateTime? postedUtc = null,
        DateTime? createdUtc = null)
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

    private async Task<List<ListingStatusChangeEntity>> GetHistory(int listingEntityId)
    {
        await using var db = await Factory().CreateDbContextAsync();
        return await db.ListingStatusChanges.Where(c => c.ListingEntityId == listingEntityId).ToListAsync();
    }

    private async Task AddHistoryRow(
        int listingEntityId, string status, string source, decimal? price, DateTime? soldDateUtc)
    {
        await using var db = await Factory().CreateDbContextAsync();
        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            ListingEntityId = listingEntityId,
            Status = status,
            Source = source,
            Price = price,
            SoldDateUtc = soldDateUtc,
            ChangedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private IDbContextFactory<EtlDbContext> Factory() =>
        _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

    private ItemDetailStore CreateStore() => new(Factory());
}
