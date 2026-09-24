using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ItemDetailStoreTests
{
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

        var targets = await store.GetListingsNeedingDetail(jobId, 1, CancellationToken.None);

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
    public async Task Should_mark_the_listing_failed_and_stamp_the_detail_fetch_time_when_a_fetch_fails()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingEntityId = await SeedListing(jobId, "detail-fetch-failed", detailFetched: false);

        await store.MarkDetailFetchFailed(listingEntityId, CancellationToken.None);

        var listing = await GetListing(listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.DescriptionStatus, Is.EqualTo("failed"));
            Assert.That(listing.DetailFetchedUtc, Is.Not.Null);
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

    private async Task<int> SeedListing(int jobId, string listingId, bool detailFetched, bool isSold = false)
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
            CreatedUtc = DateTime.UtcNow
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

    private IDbContextFactory<EtlDbContext> Factory() =>
        _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

    private ItemDetailStore CreateStore() => new(Factory());
}
