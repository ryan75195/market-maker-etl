using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SearchRescrapesPreserveEnrichmentFieldsTests
{
    private const string ListingId = "enrichment-preserve-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-preserve-enrichment-{Guid.NewGuid():N}.db");
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
    public async Task Should_keep_enrichment_fields_after_a_rescrape_whose_summary_has_them_null_or_empty()
    {
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var enriched = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/enrichment-preserve-listing-001/",
            false,
            "Good",
            "https://static.mercdn.net/console-1.jpg",
            null,
            Brand: "Sony",
            OriginalPrice: 350m,
            Category: "Consoles",
            Likes: 32,
            ImageUrls: ["https://static.mercdn.net/console-1.jpg", "https://static.mercdn.net/console-2.jpg"]);
        await store.UpsertListings(jobId, [enriched], CancellationToken.None);

        var barebonesRescrape = new ListingSummary(
            ListingId,
            "PS5 Digital (2)",
            275m,
            "USD",
            "https://www.mercari.com/us/item/enrichment-preserve-listing-001/",
            false,
            null,
            null,
            null);
        await store.UpsertListings(jobId, [barebonesRescrape], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Title, Is.EqualTo("PS5 Digital (2)"));
            Assert.That(stored.Price, Is.EqualTo(275m));
            Assert.That(stored.Condition, Is.EqualTo("Good"));
            Assert.That(stored.PrimaryImageUrl, Is.EqualTo("https://static.mercdn.net/console-1.jpg"));
            Assert.That(stored.Brand, Is.EqualTo("Sony"));
            Assert.That(stored.OriginalPrice, Is.EqualTo(350m));
            Assert.That(stored.Category, Is.EqualTo("Consoles"));
            Assert.That(stored.Likes, Is.EqualTo(32));
            Assert.That(
                stored.ImageUrls,
                Is.EqualTo(new[]
                {
                    "https://static.mercdn.net/console-1.jpg",
                    "https://static.mercdn.net/console-2.jpg"
                }));
        });
    }
}
