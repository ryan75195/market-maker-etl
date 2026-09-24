using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingDetailFieldsRoundTripThroughTheStoreTests
{
    private static readonly string CapturedPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-payload.json"));

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-detail-fields-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_image_urls_original_price_and_category_from_a_captured_search_payload_listing()
    {
        var listing = new MercariSearchParser().Parse(CapturedPayload).Listings[0];
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);

        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.ImageUrls, Is.EqualTo(listing.ImageUrls));
            Assert.That(stored.OriginalPrice, Is.EqualTo(listing.OriginalPrice));
            Assert.That(stored.Category, Is.EqualTo(listing.Category));
        });
    }
}
