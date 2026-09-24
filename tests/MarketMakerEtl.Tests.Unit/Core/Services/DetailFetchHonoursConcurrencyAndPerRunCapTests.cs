using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DetailFetchHonoursConcurrencyAndPerRunCapTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-detail-cap-{Guid.NewGuid():N}.db");
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
    public async Task Should_fetch_only_up_to_the_per_run_cap_and_leave_the_rest_for_a_later_run()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedJob(factory);

        for (var i = 0; i < 5; i++)
        {
            await SeedListing(factory, jobId, $"cap-listing-{i}");
        }

        var parser = BuildParser();
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<html/>");
        var service = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [parser], new DetailFetchOptions(4, 2));

        await service.FetchDetails(jobId, CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listings = await db.Listings.Where(l => l.ScrapeJobId == jobId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(listings.Count(l => l.DetailFetchedUtc != null), Is.EqualTo(2));
            Assert.That(listings.Count(l => l.DetailFetchedUtc == null), Is.EqualTo(3));
        });
    }

    private static IItemPageParser BuildParser()
    {
        var parser = Substitute.For<IItemPageParser>();
        parser.Marketplace.Returns(Marketplace.Mercari);
        parser.Parse(Arg.Any<string>()).Returns(new ItemPageListing(
            ListingId: null,
            Title: "Item",
            Price: 1m,
            Currency: "USD",
            Condition: null,
            BuyingFormat: null,
            Status: "Active",
            SoldPrice: null,
            SoldDate: null,
            Seller: "Seller",
            PrimaryImageUrl: null));
        return parser;
    }

    private static async Task<int> SeedJob(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task SeedListing(IDbContextFactory<EtlDbContext> factory, int jobId, string listingId)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Listings.Add(new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = $"https://www.mercari.com/us/item/{listingId}/",
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
