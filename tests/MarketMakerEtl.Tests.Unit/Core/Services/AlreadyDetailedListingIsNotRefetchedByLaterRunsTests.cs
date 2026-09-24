using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class AlreadyDetailedListingIsNotRefetchedByLaterRunsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-already-detailed-{Guid.NewGuid():N}.db");
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
    public async Task Should_not_fetch_a_listing_again_once_it_already_has_a_detail_fetch_timestamp()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedJob(factory);
        var listingEntityId = await SeedAlreadyDetailedListing(factory, jobId);
        var client = Substitute.For<IScrapeClient>();
        var service = new ItemDetailFetchService(
            new ItemDetailStore(factory), client, [new MercariItemPageParser()], new DetailFetchOptions(4, 50, 3));

        await service.FetchDetails(jobId, CancellationToken.None);
        await service.FetchDetails(jobId, CancellationToken.None);

        await client.DidNotReceive().GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var db = await factory.CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingEntityId);
        Assert.Multiple(() =>
        {
            Assert.That(listing.Description, Is.EqualTo("Pre-existing description"));
            Assert.That(listing.DescriptionStatus, Is.EqualTo("ok"));
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

    private static async Task<int> SeedAlreadyDetailedListing(IDbContextFactory<EtlDbContext> factory, int jobId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = "already-detailed-1",
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = "https://www.mercari.com/us/item/already-detailed-1/",
            ItemStatus = "Active",
            Description = "Pre-existing description",
            DescriptionStatus = "ok",
            DetailFetchedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }
}
