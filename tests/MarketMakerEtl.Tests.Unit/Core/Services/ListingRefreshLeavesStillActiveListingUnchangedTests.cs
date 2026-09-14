using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshLeavesStillActiveListingUnchangedTests
{
    private const string ActiveUrl = "https://www.ebay.co.uk/itm/333333333333";

    private const string ActivePage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Sony PlayStation 5 Disc Console</h1>
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
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-active-{Guid.NewGuid():N}.db");
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
    public async Task Should_leave_a_listing_that_is_still_active_without_a_status_change_record()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [ActiveUrl] = ActivePage });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), new DelegatingItemPageParser());

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var changes = await verify.ListingStatusChanges.ToListAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.Empty);
            Assert.That(listing.ItemStatus, Is.Not.EqualTo("Sold"));
            Assert.That(listing.ItemStatus, Is.Not.EqualTo("Ended"));
        });
    }

    private static async Task<int> SeedListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "still-active-listing-333",
            Url = ActiveUrl,
            ItemStatus = null,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
