using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class RefreshSelectsTheItemParserForTheListingsMarketplaceTests
{
    private const string EbayUrl = "https://www.ebay.co.uk/itm/111111111111";
    private const string MercariUrl = "https://www.mercari.com/us/item/m92390261760/";
    private const string EbayHtml = "<html>ebay item page body</html>";
    private const string MercariHtml = "<html>mercari item page body</html>";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-refresh-marketplace-{Guid.NewGuid():N}.db");
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
    public async Task Should_parse_each_listing_with_the_parser_for_its_marketplace()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var ebayListingId = await SeedActiveListing(factory, "ebay-listing-111", EbayUrl, Marketplace.Ebay);
        var mercariListingId = await SeedActiveListing(factory, "m92390261760", MercariUrl, Marketplace.Mercari);
        var client = new StubScrapeClient(new Dictionary<string, string>
        {
            [EbayUrl] = EbayHtml,
            [MercariUrl] = MercariHtml
        });
        var ebayParser = new RecordingItemPageParser(Marketplace.Ebay, "Ended");
        var mercariParser = new RecordingItemPageParser(Marketplace.Mercari, "Sold");
        var service = new ListingRefreshService(
            client,
            new ScrapeStore(factory, new ScrapeRunStateService()),
            [ebayParser, mercariParser]);

        await service.RefreshActiveListings(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var ebay = await db.Listings.SingleAsync(l => l.Id == ebayListingId);
        var mercari = await db.Listings.SingleAsync(l => l.Id == mercariListingId);

        Assert.Multiple(() =>
        {
            Assert.That(ebayParser.RequestedHtml, Is.EqualTo(new[] { EbayHtml }));
            Assert.That(mercariParser.RequestedHtml, Is.EqualTo(new[] { MercariHtml }));
            Assert.That(ebay.ItemStatus, Is.EqualTo("Ended"));
            Assert.That(mercari.ItemStatus, Is.EqualTo("Sold"));
        });
    }

    [Test]
    public void Should_register_item_page_parsers_for_both_marketplaces()
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        using var provider = services.BuildServiceProvider();

        var marketplaces = provider.GetServices<IItemPageParser>()
            .Select(parser => parser.Marketplace)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(marketplaces, Does.Contain(Marketplace.Ebay));
            Assert.That(marketplaces, Does.Contain(Marketplace.Mercari));
        });
    }

    private static async Task<int> SeedActiveListing(
        IDbContextFactory<EtlDbContext> factory,
        string listingId,
        string url,
        Marketplace marketplace)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            Url = url,
            ItemStatus = "Active",
            Marketplace = marketplace,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private sealed class RecordingItemPageParser : IItemPageParser
    {
        private readonly List<string> _requestedHtml = [];
        private readonly string _status;

        public RecordingItemPageParser(Marketplace marketplace, string status)
        {
            Marketplace = marketplace;
            _status = status;
        }

        public Marketplace Marketplace { get; }

        public IReadOnlyList<string> RequestedHtml => _requestedHtml;

        public ItemPageListing? Parse(string html)
        {
            _requestedHtml.Add(html);
            return new ItemPageListing(
                ListingId: null,
                Title: "Recorded item",
                Price: 10m,
                Currency: null,
                Condition: null,
                BuyingFormat: null,
                Status: _status,
                SoldPrice: null,
                SoldDate: null,
                Seller: null,
                PrimaryImageUrl: null,
                Brand: null);
        }
    }
}
