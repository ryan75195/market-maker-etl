using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class NewListingsGetItemDetailFetchedAfterARunTests
{
    private const string ListingUrl = "https://www.mercari.com/us/item/m71344610988/";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-new-listing-detail-{Guid.NewGuid():N}.db");
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
    public async Task Should_populate_description_images_shipping_cost_and_seller_from_the_item_page_after_a_run()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedJob(factory);
        var listingEntityId = await SeedListing(factory, jobId, "m71344610988", ListingUrl);
        var html = ReadFixture("item-active-m71344610988.html");
        var client = new StubScrapeClient(new Dictionary<string, string> { [ListingUrl] = html });
        var service = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [new MercariItemPageParser()], new DetailFetchOptions(4, 50, 3));

        await service.FetchDetails(jobId, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.Description, Does.StartWith("Hi!"));
            Assert.That(listing.Description, Does.Contain("PlayStation 5 Digital Console"));
            Assert.That(listing.DescriptionStatus, Is.EqualTo("ok"));
            Assert.That(ListingImageUrlsJson.Deserialize(listing.ImageUrls), Has.Count.EqualTo(8));
            Assert.That(listing.ShippingCost, Is.EqualTo(0m));
            Assert.That(listing.Seller, Is.EqualTo("Nerd Mom Electronics"));
            Assert.That(listing.OriginalPrice, Is.EqualTo(399.00m));
            Assert.That(listing.DetailFetchedUtc, Is.Not.Null);
        });
    }

    private static async Task<int> SeedJob(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<int> SeedListing(
        IDbContextFactory<EtlDbContext> factory, int jobId, string listingId, string url)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = url,
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
