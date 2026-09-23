using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSoldRefreshStoresUtcSoldDateTests
{
    private const string SoldUrl = "https://www.mercari.com/us/item/m44688360101/";

    private static readonly string SoldItemPage =
        File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "item-sold-m44688360101.html"));

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-mercari-sold-{Guid.NewGuid():N}.db");
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
    public async Task Should_store_the_utc_sale_time_after_a_mercari_sold_recheck()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [SoldUrl] = SoldItemPage });
        var service = new ListingRefreshService(
            client, new ScrapeStore(factory, new ScrapeRunStateService()), [new MercariItemPageParser()]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);

        Assert.That(listing.SoldDate, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(listing.SoldPrice, Is.EqualTo(140.25m));
            Assert.That(listing.SoldDate!.Value.Year, Is.EqualTo(2026));
            Assert.That(listing.SoldDate.Value.Month, Is.EqualTo(9));
            Assert.That(listing.SoldDate.Value.Day, Is.EqualTo(23));
            Assert.That(listing.SoldDate.Value.Hour, Is.EqualTo(22));
            Assert.That(listing.SoldDate.Value.Minute, Is.EqualTo(21));
            Assert.That(listing.SoldDate.Value.Second, Is.EqualTo(59));
        });
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "m44688360101",
            Url = SoldUrl,
            ItemStatus = "Active",
            Marketplace = Marketplace.Mercari,
            Price = 140.25m,
            IsSold = false,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
