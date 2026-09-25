using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ItemDetailStoreSegmentationFieldsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-item-detail-segmentation-{Guid.NewGuid():N}.db");
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
    public async Task Should_upsert_the_seller_and_persist_item_detail_raw_json_when_applying_an_item_detail()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ItemDetailStore(factory);
        var listingEntityId = await SeedListing("item-detail-segmentation");
        var detail = BuildItemPageListing();

        await store.ApplyItemDetail(listingEntityId, detail, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingEntityId);
        var seller = await db.Sellers.SingleAsync(s => s.SellerId == 865070813L);
        var rawDataStore = new ListingRawDataStore(factory);
        var raw = await rawDataStore.GetRawData(listingEntityId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(listing.SellerId, Is.EqualTo(865070813L));
            Assert.That(listing.CategoryId, Is.EqualTo(797));
            Assert.That(listing.BrandId, Is.EqualTo(5058));
            Assert.That(seller.Name, Is.EqualTo("Nerd Mom Electronics"));
            Assert.That(seller.NumSales, Is.EqualTo(317));
            Assert.That(raw!.ItemDetailJson, Is.EqualTo("""{"id":"m71344610988"}"""));
        });
    }

    [Test]
    public async Task Should_refresh_seller_stats_on_a_later_item_detail_fetch()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var store = new ItemDetailStore(factory);
        var listingEntityId = await SeedListing("item-detail-seller-refresh");
        await store.ApplyItemDetail(listingEntityId, BuildItemPageListing(numSales: 100), CancellationToken.None);

        await store.ApplyItemDetail(listingEntityId, BuildItemPageListing(numSales: 150), CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var seller = await db.Sellers.SingleAsync(s => s.SellerId == 865070813L);
        Assert.That(seller.NumSales, Is.EqualTo(150));
    }

    private static ItemPageListing BuildItemPageListing(int numSales = 317) =>
        new(
            ListingId: null,
            Title: "Item Title",
            Price: 15.00m,
            Currency: "USD",
            Condition: "Good",
            BuyingFormat: null,
            Status: null,
            SoldPrice: null,
            SoldDate: null,
            Seller: "Nerd Mom Electronics",
            PrimaryImageUrl: "https://img/1.jpg",
            CategoryId: 797,
            BrandId: 5058,
            SellerProfile: new MercariSellerProfile(865070813L, "Nerd Mom Electronics", numSales, 328, 327, 5.0, false),
            RawJson: """{"id":"m71344610988"}""");

    private async Task<int> SeedListing(string listingId)
    {
        await using var db = await Factory().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = job.Id,
            Url = $"https://x/itm/{listingId}",
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private IDbContextFactory<EtlDbContext> Factory() =>
        _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
}
