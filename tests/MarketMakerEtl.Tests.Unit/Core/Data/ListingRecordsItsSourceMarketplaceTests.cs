using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingRecordsItsSourceMarketplaceTests
{
    private const string MercariListingId = "m123456789";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-listing-marketplace-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_the_marketplace_a_listing_was_collected_from()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobId = await SeedMercariJob(factory);
        var listing = new ListingSummary(
            MercariListingId,
            "Mercari Switch",
            12000m,
            "JPY",
            "https://example.test/m/1",
            false,
            null,
            null,
            null);

        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var stored = await db.Listings.SingleAsync(l => l.ListingId == MercariListingId);

        Assert.Multiple(() =>
        {
            Assert.That(stored.Marketplace, Is.EqualTo(Marketplace.Mercari));
            Assert.That(stored.Title, Is.EqualTo("Mercari Switch"));
        });
    }

    private static async Task<int> SeedMercariJob(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var job = new ScrapeJobEntity
        {
            SearchTerm = "mercari-listing",
            Marketplace = Marketplace.Mercari,
            CreatedUtc = DateTime.UtcNow
        };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
