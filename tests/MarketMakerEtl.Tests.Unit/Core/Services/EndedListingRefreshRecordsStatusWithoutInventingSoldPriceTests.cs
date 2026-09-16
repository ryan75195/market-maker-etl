using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EndedListingRefreshRecordsStatusWithoutInventingSoldPriceTests
{
    private const string EndedUrl = "https://www.ebay.co.uk/itm/443333333333";

    private const string EndedPage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Nintendo Switch OLED Console</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£189.00</span>
        </div>
        <div class="x-photos-cvip">
          <span class="ux-textspans">ENDED</span>
        </div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-ended-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_the_ended_status_without_a_sold_price_after_an_ended_recheck()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [EndedUrl] = EndedPage });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), [new DelegatingItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);
        var changes = await verify.ListingStatusChanges
            .Where(c => c.ListingEntityId == listingId)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(listing.ItemStatus, Is.EqualTo("Ended"));
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.IsSold, Is.False);
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Status, Is.EqualTo("Ended"));
        });
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "ended-listing-443",
            Url = EndedUrl,
            ItemStatus = "Active",
            Price = 259m,
            IsSold = false,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
