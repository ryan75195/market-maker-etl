using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SoldSearchListingSoldDateBackfillTests
{
    private const string ListingId = "sold-date-backfill-listing-001";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-sold-date-backfill-{Guid.NewGuid():N}.db");
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
    public async Task Should_store_the_sold_date_a_sold_search_listing_arrives_with()
    {
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var soldDate = new DateTime(2026, 9, 20, 8, 15, 0, DateTimeKind.Utc);
        var soldListing = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/sold-date-backfill-listing-001/",
            true,
            null,
            null,
            null,
            SoldDate: soldDate);

        await store.UpsertListings(jobId, [soldListing], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.That(stored.SoldDate, Is.EqualTo(soldDate));
    }

    [Test]
    public async Task Should_keep_a_detail_set_sold_date_when_a_later_sold_search_reobserves_the_listing()
    {
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var detailSoldDate = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var soldFromDetail = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/sold-date-backfill-listing-001/",
            true,
            null,
            null,
            null,
            SoldDate: detailSoldDate);
        await store.UpsertListings(jobId, [soldFromDetail], CancellationToken.None);

        var laterSoldSearchSighting = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/sold-date-backfill-listing-001/",
            true,
            null,
            null,
            null,
            SoldDate: new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));
        await store.UpsertListings(jobId, [laterSoldSearchSighting], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.That(stored.SoldDate, Is.EqualTo(detailSoldDate));
    }

    [Test]
    public async Task Should_backfill_a_missing_sold_date_when_an_already_sold_listing_is_seen_again()
    {
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
        var jobId = await store.EnsureJob("ps5", CancellationToken.None);
        var soldWithoutDate = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/sold-date-backfill-listing-001/",
            true,
            null,
            null,
            null);
        await store.UpsertListings(jobId, [soldWithoutDate], CancellationToken.None);

        var backfillSoldDate = new DateTime(2026, 9, 22, 6, 30, 0, DateTimeKind.Utc);
        var seenAgainWithDate = new ListingSummary(
            ListingId,
            "PS5 Digital",
            300m,
            "USD",
            "https://www.mercari.com/us/item/sold-date-backfill-listing-001/",
            true,
            null,
            null,
            null,
            SoldDate: backfillSoldDate);
        await store.UpsertListings(jobId, [seenAgainWithDate], CancellationToken.None);

        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();
        Assert.That(stored.SoldDate, Is.EqualTo(backfillSoldDate));
    }
}
