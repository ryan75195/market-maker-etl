using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshRecordsStatusChangeForNewlySoldListingTests
{
    private const string StillActiveUrl = "https://www.ebay.co.uk/itm/111111111111";
    private const string BecameSoldUrl = "https://www.ebay.co.uk/itm/222222222222";

    private const string StillActivePage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Apple iPhone 15 Pro 256GB</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£749.99</span>
        </div>
        """;

    private const string BecameSoldPage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Sony WH-1000XM5 Headphones</h1>
        </div>
        <div class="x-photos-cvip">
          <span class="ux-textspans">SOLD</span>
        </div>
        <div class="d-top-panel-message">This listing sold on Fri, 11 Sep at 6:09 PM.</div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-sold-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_one_status_change_only_for_the_listing_whose_page_became_sold()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var stillActiveId = await SeedListing(factory, "active-listing-111", StillActiveUrl);
        var becameSoldId = await SeedListing(factory, "sold-listing-222", BecameSoldUrl);
        var client = new StubScrapeClient(new Dictionary<string, string>
        {
            [StillActiveUrl] = StillActivePage,
            [BecameSoldUrl] = BecameSoldPage
        });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), new DelegatingItemPageParser());

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var changes = await verify.ListingStatusChanges.ToListAsync();
        var becameSold = await verify.Listings.SingleAsync(l => l.Id == becameSoldId);
        var stillActiveChanged = await verify.ListingStatusChanges.AnyAsync(c => c.ListingEntityId == stillActiveId);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].ListingEntityId, Is.EqualTo(becameSoldId));
            Assert.That(changes[0].Status, Is.EqualTo("Sold"));
            Assert.That(becameSold.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(stillActiveChanged, Is.False);
        });
    }

    private static async Task<int> SeedListing(
        IDbContextFactory<EtlDbContext> factory,
        string listingId,
        string url)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            Url = url,
            ItemStatus = null,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
