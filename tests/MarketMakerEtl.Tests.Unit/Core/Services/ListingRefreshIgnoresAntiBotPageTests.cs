using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshIgnoresAntiBotPageTests
{
    private const string GatedUrl = "https://www.ebay.co.uk/itm/444444444444";

    private const string AntiBotPage = """
        <html>
          <body>
            <div class="d-top-panel-message">Pardon Our Interruption. Please verify yourself to continue.</div>
          </body>
        </html>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-gated-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_no_status_change_when_the_item_page_is_an_anti_bot_page()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [GatedUrl] = AntiBotPage });
        var service = new ListingRefreshService(client, new ScrapeStore(factory, new ScrapeRunStateService()), [new DelegatingItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var changes = await verify.ListingStatusChanges.Where(c => c.ListingEntityId == listingId).ToListAsync();
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
            ListingId = "gated-listing-444",
            Url = GatedUrl,
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
