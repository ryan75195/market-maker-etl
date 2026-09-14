using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingRefreshTargetCarriesItsMarketplaceTests
{
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
    public async Task Should_return_refresh_targets_with_the_marketplace_of_their_listing()
    {
        var store = CreateStore();
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Listings.AddRange(
                new ListingEntity
                {
                    ListingId = "refresh-mercari",
                    Url = "https://example.test/m/refresh",
                    ItemStatus = null,
                    Marketplace = Marketplace.Mercari,
                    CreatedUtc = DateTime.UtcNow
                },
                new ListingEntity
                {
                    ListingId = "refresh-ebay",
                    Url = "https://example.test/e/refresh",
                    ItemStatus = "Active",
                    Marketplace = Marketplace.Ebay,
                    CreatedUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var targets = await store.GetActiveListings(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                targets.Single(t => t.ListingId == "refresh-mercari").Marketplace,
                Is.EqualTo(Marketplace.Mercari));
            Assert.That(
                targets.Single(t => t.ListingId == "refresh-ebay").Marketplace,
                Is.EqualTo(Marketplace.Ebay));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
