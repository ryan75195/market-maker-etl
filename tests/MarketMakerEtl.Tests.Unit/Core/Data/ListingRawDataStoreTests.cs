using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingRawDataStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-raw-data-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_decompress_stored_search_and_item_detail_json_for_a_listing()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingEntityId = 0;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = new ListingEntity
            {
                ListingId = "raw-data-round-trip",
                Url = "https://x/itm/raw-data-round-trip",
                CreatedUtc = DateTime.UtcNow
            };
            db.Listings.Add(listing);
            await db.SaveChangesAsync();
            listingEntityId = listing.Id;

            db.ListingRawData.Add(new ListingRawDataEntity
            {
                ListingEntityId = listingEntityId,
                SearchItemJsonGzip = GzipJson.Compress("""{"id":"m1"}"""),
                ItemDetailJsonGzip = GzipJson.Compress("""{"id":"m1","status":"on_sale"}""")
            });
            await db.SaveChangesAsync();
        }

        var store = new ListingRawDataStore(factory);
        var raw = await store.GetRawData(listingEntityId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(raw, Is.Not.Null);
            Assert.That(raw!.SearchItemJson, Is.EqualTo("""{"id":"m1"}"""));
            Assert.That(raw.ItemDetailJson, Is.EqualTo("""{"id":"m1","status":"on_sale"}"""));
        });
    }

    [Test]
    public async Task Should_return_null_when_no_raw_data_row_exists_for_the_listing()
    {
        var store = new ListingRawDataStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

        var raw = await store.GetRawData(999, CancellationToken.None);

        Assert.That(raw, Is.Null);
    }
}
