using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SoldDateAndSoldPriceAreReadableFromGetListingsTests
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
    public async Task Should_return_the_sold_date_and_sold_price_for_a_sold_listing()
    {
        var store = CreateStore();
        var jobId = await store.EnsureJob("air jordan 1", CancellationToken.None);
        var soldDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext())
        {
            db.Listings.Add(new ListingEntity
            {
                ListingId = "m123456789",
                ScrapeJobId = jobId,
                Marketplace = Marketplace.Mercari,
                Title = "Air Jordan 1",
                Price = 120m,
                Currency = "USD",
                Url = "https://www.mercari.com/us/item/m123456789/",
                IsSold = true,
                ItemStatus = "Sold",
                SoldPrice = 115m,
                SoldDate = soldDate,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var stored = listings.Single();

        Assert.Multiple(() =>
        {
            Assert.That(stored.SoldPrice, Is.EqualTo(115m));
            Assert.That(stored.SoldDate, Is.EqualTo(soldDate));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
