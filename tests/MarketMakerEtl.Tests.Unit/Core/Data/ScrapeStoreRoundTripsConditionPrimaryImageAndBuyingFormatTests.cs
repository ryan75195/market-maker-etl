using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ScrapeStoreRoundTripsConditionPrimaryImageAndBuyingFormatTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-{Guid.NewGuid():N}.db");
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
    public async Task Should_round_trip_condition_primary_image_and_buying_format_through_the_store()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var listing = new ListingSummary(
            ListingId: "111111111111",
            Title: "PS5",
            Price: 100m,
            Currency: "GBP",
            Url: "https://x/itm/111111111111",
            IsSold: false,
            Condition: "New",
            PrimaryImageUrl: "https://i.ebayimg.com/images/g/xyz789/s-l500.jpg",
            BuyingFormat: "Auction");

        await store.UpsertListings(jobId, [listing], CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var stored = listings.Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Condition, Is.EqualTo("New"));
            Assert.That(stored.PrimaryImageUrl, Is.EqualTo("https://i.ebayimg.com/images/g/xyz789/s-l500.jpg"));
            Assert.That(stored.BuyingFormat, Is.EqualTo("Auction"));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
