using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeStoreSegmentationFieldsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-segmentation-{Guid.NewGuid():N}.db");
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
    public async Task Should_store_and_round_trip_segmentation_fields_for_a_new_listing()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var summary = BuildSummary("segmentation-new");

        await store.UpsertListings(jobId, [summary], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var listing = listings.Single();
        Assert.Multiple(() =>
        {
            Assert.That(listing.CategoryId, Is.EqualTo(797));
            Assert.That(listing.CategoryHierarchy!.Level0Name, Is.EqualTo("Electronics"));
            Assert.That(listing.CategoryHierarchy.Level1Name, Is.EqualTo("Video games & consoles"));
            Assert.That(listing.CategoryHierarchy.Level2Name, Is.EqualTo("Consoles"));
            Assert.That(listing.BrandId, Is.EqualTo(5058));
            Assert.That(listing.ConditionId, Is.EqualTo(3));
            Assert.That(listing.SizeName, Is.EqualTo("6 (39)"));
            Assert.That(listing.ColorName, Is.EqualTo("Black"));
            Assert.That(listing.ShippingPayer, Is.EqualTo("seller"));
            Assert.That(listing.SellerId, Is.EqualTo(865070813L));
            Assert.That(listing.Attributes!["Model"], Is.EqualTo("PULSE 3D"));
        });
    }

    [Test]
    public async Task Should_not_erase_segmentation_fields_when_a_rescrape_omits_them()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var original = BuildSummary("segmentation-preserve");
        await store.UpsertListings(jobId, [original], CancellationToken.None);

        var thin = new ListingSummary(
            "segmentation-preserve", "PS5", 90m, "USD", "https://x/itm/segmentation-preserve", false, null, null, null);
        await store.UpsertListings(jobId, [thin], CancellationToken.None);

        var listing = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(listing.Price, Is.EqualTo(90m));
            Assert.That(listing.CategoryId, Is.EqualTo(797));
            Assert.That(listing.BrandId, Is.EqualTo(5058));
            Assert.That(listing.SellerId, Is.EqualTo(865070813L));
            Assert.That(listing.Attributes!["Model"], Is.EqualTo("PULSE 3D"));
        });
    }

    [Test]
    public async Task Should_persist_raw_search_json_for_a_listing_and_update_it_on_rescrape()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var original = BuildSummary("segmentation-raw-json") with { RawJson = """{"id":"segmentation-raw-json","v":1}""" };
        await store.UpsertListings(jobId, [original], CancellationToken.None);

        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var entity = await db.Listings.SingleAsync(l => l.ListingId == "segmentation-raw-json");
            var rawDataStore = new ListingRawDataStore(factory);
            var raw = await rawDataStore.GetRawData(entity.Id, CancellationToken.None);
            Assert.That(raw!.SearchItemJson, Is.EqualTo("""{"id":"segmentation-raw-json","v":1}"""));
        }

        var updated = original with { RawJson = """{"id":"segmentation-raw-json","v":2}""" };
        await store.UpsertListings(jobId, [updated], CancellationToken.None);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var updatedEntity = await verifyDb.Listings.SingleAsync(l => l.ListingId == "segmentation-raw-json");
        var updatedRawStore = new ListingRawDataStore(factory);
        var updatedRaw = await updatedRawStore.GetRawData(updatedEntity.Id, CancellationToken.None);
        Assert.That(updatedRaw!.SearchItemJson, Is.EqualTo("""{"id":"segmentation-raw-json","v":2}"""));
    }

    private static ListingSummary BuildSummary(string listingId) =>
        new(
            listingId,
            "PS5",
            100m,
            "USD",
            $"https://x/itm/{listingId}",
            false,
            null,
            null,
            null,
            CategoryId: 797,
            CategoryHierarchy: new MercariCategoryHierarchy(7, "Electronics", 84, "Video games & consoles", 797, "Consoles"),
            BrandId: 5058,
            ConditionId: 3,
            SizeName: "6 (39)",
            ColorName: "Black",
            ShippingPayer: "seller",
            SellerId: 865070813L,
            Attributes: new Dictionary<string, string> { ["Model"] = "PULSE 3D" });

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new MarketMakerEtl.Core.Services.ScrapeRunStateService());
}
