using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FailedItemFetchMarksListingFailedWithoutFailingTheRunTests
{
    private const string BlockedUrl = "https://www.mercari.com/us/item/blocked-listing/";
    private const string OkUrl = "https://www.mercari.com/us/item/m71344610988/";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-failed-detail-{Guid.NewGuid():N}.db");
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
    public async Task Should_mark_the_blocked_listing_failed_and_still_apply_detail_to_the_listing_that_succeeds()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedJob(factory);
        var blockedListingId = await SeedListing(factory, jobId, "blocked-listing", BlockedUrl);
        var okListingId = await SeedListing(factory, jobId, "m71344610988", OkUrl);
        var client = new SelectivelyFailingScrapeClient(
            failingUrl: BlockedUrl,
            pages: new Dictionary<string, string> { [OkUrl] = ReadFixture("item-active-m71344610988.html") });
        var service = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [new MercariItemPageParser()], new DetailFetchOptions(4, 50));

        Assert.DoesNotThrowAsync(() => service.FetchDetails(jobId, CancellationToken.None));

        await using var db = await factory.CreateDbContextAsync();
        var blocked = await db.Listings.SingleAsync(l => l.Id == blockedListingId);
        var ok = await db.Listings.SingleAsync(l => l.Id == okListingId);
        Assert.Multiple(() =>
        {
            Assert.That(blocked.DescriptionStatus, Is.EqualTo("failed"));
            Assert.That(blocked.DetailFetchedUtc, Is.Not.Null);
            Assert.That(ok.DescriptionStatus, Is.EqualTo("ok"));
            Assert.That(ok.Description, Does.StartWith("Hi!"));
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

    private sealed class SelectivelyFailingScrapeClient : IScrapeClient
    {
        private readonly string _failingUrl;
        private readonly IReadOnlyDictionary<string, string> _pages;

        public SelectivelyFailingScrapeClient(string failingUrl, IReadOnlyDictionary<string, string> pages)
        {
            _failingUrl = failingUrl;
            _pages = pages;
        }

        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            string.Equals(url, _failingUrl, StringComparison.Ordinal)
                ? throw new InvalidOperationException("blocked")
                : Task.FromResult(_pages[url]);
    }
}
