using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshServiceTests
{
    private const string FirstUrl = "https://www.ebay.co.uk/itm/777777777777";
    private const string SecondUrl = "https://www.ebay.co.uk/itm/888888888888";

    private const string ActivePage = """
        <div class="x-item-title">
          <h1 class="x-item-title__mainTitle">Canon EOS R6 Camera Body</h1>
        </div>
        <div class="x-price-primary">
          <span class="x-price-primary__price">£1,299.00</span>
        </div>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-service-{Guid.NewGuid():N}.db");
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
    public async Task Should_fetch_the_item_page_of_every_stored_active_listing()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedListing(factory, "refresh-first-777", FirstUrl);
        await SeedListing(factory, "refresh-second-888", SecondUrl);
        var client = new StubScrapeClient(new Dictionary<string, string>
        {
            [FirstUrl] = ActivePage,
            [SecondUrl] = ActivePage
        });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), [new DelegatingItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        Assert.That(client.RequestedUrls, Is.EquivalentTo(new[] { FirstUrl, SecondUrl }));
    }

    private static async Task SeedListing(
        IDbContextFactory<EtlDbContext> factory,
        string listingId,
        string url)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Listings.Add(new ListingEntity
        {
            ListingId = listingId,
            Url = url,
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
