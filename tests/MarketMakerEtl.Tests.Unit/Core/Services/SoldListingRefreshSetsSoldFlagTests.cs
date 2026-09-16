using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldListingRefreshSetsSoldFlagTests
{
    private const string SoldUrl = "https://www.ebay.co.uk/itm/442222222222";

    private const string SoldPage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Apple iPad Air 11-inch</h1>
        </div>
        <div class="x-photos-cvip">
          <span class="ux-textspans">SOLD</span>
        </div>
        <div class="x-item-condensed-card__sold-price">£399.00</div>
        <div class="d-top-panel-message">This listing sold on Fri, 11 Sep at 6:09 PM.</div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-sold-flag-{Guid.NewGuid():N}.db");
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
    public async Task Should_set_the_stored_sold_flag_after_a_sold_recheck()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [SoldUrl] = SoldPage });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), [new DelegatingItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);

        Assert.That(listing.IsSold, Is.True);
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "sold-flag-listing-442",
            Url = SoldUrl,
            ItemStatus = "Active",
            Price = 349m,
            IsSold = false,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
