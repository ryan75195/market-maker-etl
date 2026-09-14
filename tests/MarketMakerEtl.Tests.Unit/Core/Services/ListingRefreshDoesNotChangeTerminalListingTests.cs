using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshDoesNotChangeTerminalListingTests
{
    private const string SoldUrl = "https://www.ebay.co.uk/itm/555555555555";
    private const string EndedUrl = "https://www.ebay.co.uk/itm/666666666666";

    private const string ActiveLookingPage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Nintendo Switch OLED Console</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£259.00</span>
        </div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-terminal-{Guid.NewGuid():N}.db");
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
    public async Task Should_leave_sold_and_ended_listings_unchanged_on_recheck()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var soldId = await SeedListing(factory, "terminal-sold-555", SoldUrl, "Sold");
        var endedId = await SeedListing(factory, "terminal-ended-666", EndedUrl, "Ended");
        var client = new StubScrapeClient(new Dictionary<string, string>
        {
            [SoldUrl] = ActiveLookingPage,
            [EndedUrl] = ActiveLookingPage
        });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), new DelegatingItemPageParser());

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var changes = await verify.ListingStatusChanges.ToListAsync();
        var sold = await verify.Listings.SingleAsync(l => l.Id == soldId);
        var ended = await verify.Listings.SingleAsync(l => l.Id == endedId);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(sold.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(ended.ItemStatus, Is.EqualTo("Ended"));
        });
    }

    private static async Task<int> SeedListing(
        IDbContextFactory<EtlDbContext> factory,
        string listingId,
        string url,
        string status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            Url = url,
            ItemStatus = status,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
