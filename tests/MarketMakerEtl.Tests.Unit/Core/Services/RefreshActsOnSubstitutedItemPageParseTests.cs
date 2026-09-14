using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class RefreshActsOnSubstitutedItemPageParseTests
{
    private const string ListingUrl = "https://www.ebay.co.uk/itm/555555555555";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-substituted-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_the_status_observed_through_a_substituted_item_page_parse()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingId = await SeedActiveListing(factory);
        var client = new StubScrapeClient(new Dictionary<string, string> { [ListingUrl] = "<html/>" });
        var parser = Substitute.For<IItemPageParser>();
        parser.Parse(Arg.Any<string>()).Returns(new ItemPageListing(
            "555555555555",
            "Substituted Camera Body",
            123.45m,
            "GBP",
            "Used",
            "Buy It Now",
            "Sold",
            99.99m,
            "Fri, 11 Sep",
            "Substituted Seller",
            "https://img.example/substituted.jpg"));
        var service = new ListingRefreshService(
            client,
            new ScrapeStore(factory, new ScrapeRunStateService()),
            parser);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var listing = await verify.Listings.SingleAsync(l => l.Id == listingId);
        var changes = await verify.ListingStatusChanges
            .Where(c => c.ListingEntityId == listingId)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(listing.ItemStatus, Is.EqualTo("Sold"));
            Assert.That(listing.Price, Is.EqualTo(123.45m));
            Assert.That(listing.SoldPrice, Is.EqualTo(99.99m));
            Assert.That(listing.Seller, Is.EqualTo("Substituted Seller"));
            Assert.That(changes.Select(c => c.Status), Is.EqualTo(new[] { "Sold" }));
        });
    }

    private static async Task<int> SeedActiveListing(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "substituted-listing-555",
            Url = ListingUrl,
            ItemStatus = "Active",
            Price = 1m,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
