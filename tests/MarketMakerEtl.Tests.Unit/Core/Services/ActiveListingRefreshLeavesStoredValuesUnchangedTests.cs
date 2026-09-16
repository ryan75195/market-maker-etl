using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ActiveListingRefreshLeavesStoredValuesUnchangedTests
{
    private const string ActiveUrl = "https://www.ebay.co.uk/itm/444444444444";

    private const string ActivePage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Canon EOS R6 Camera Body</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£389.00</span>
        </div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-active-values-{Guid.NewGuid():N}.db");
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
    public async Task Should_leave_a_still_active_listing_stored_values_unchanged_after_a_recheck()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [ActiveUrl] = ActivePage });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), [new DelegatingItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);

        Assert.Multiple(() =>
        {
            Assert.That(listing.ItemStatus, Is.EqualTo("Active"));
            Assert.That(listing.Price, Is.EqualTo(349m));
            Assert.That(listing.IsSold, Is.False);
            Assert.That(listing.SoldPrice, Is.Null);
            Assert.That(listing.SoldDate, Is.Null);
            Assert.That(listing.Seller, Is.Null);
        });
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "active-values-listing-444",
            Url = ActiveUrl,
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
