using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingStatusChangeHistoryStoresRecordsTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-status-history-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
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
    public async Task Should_store_and_read_back_a_status_change_record_against_a_listing()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await factory.ApplyMigrations(CancellationToken.None);

        var changedUtc = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        var listingId = 0;
        var changeId = 0;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var listing = new ListingEntity
            {
                ListingId = "history-listing-888",
                ScrapeJobId = 0,
                Title = "History PS5",
                ItemStatus = "Active",
                CreatedUtc = DateTime.UtcNow
            };
            db.Listings.Add(listing);
            await db.SaveChangesAsync();
            listingId = listing.Id;

            var change = new ListingStatusChangeEntity
            {
                ListingEntityId = listing.Id,
                Status = "Sold",
                ChangedUtc = changedUtc
            };
            db.ListingStatusChanges.Add(change);
            await db.SaveChangesAsync();
            changeId = change.Id;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.ListingStatusChanges.SingleAsync(c => c.Id == changeId);

            Assert.Multiple(() =>
            {
                Assert.That(stored.ListingEntityId, Is.EqualTo(listingId));
                Assert.That(stored.Status, Is.EqualTo("Sold"));
                Assert.That(stored.ChangedUtc, Is.EqualTo(changedUtc));
            });
        }
    }
}
