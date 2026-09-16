using System.Data.Common;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingBrandRoundTripsThroughTheStoreTests
{
    private const string PreBrandMigration = "20260914000000_AddMarketplace";
    private const string LegacyListingId = "legacy-brand-listing";
    private const string BrandedListingId = "branded-listing-001";
    private const string UnbrandedListingId = "unbranded-listing-002";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-listing-brand-{Guid.NewGuid():N}.db");
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
    public async Task Should_round_trip_a_listing_brand_through_the_store()
    {
        await using (var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var store = CreateStore();
        var jobId = await store.EnsureJob("console", CancellationToken.None);
        await store.UpsertListings(
            jobId,
            [
                BuildListing(BrandedListingId, "Nintendo"),
                BuildListing(UnbrandedListingId, null)
            ],
            CancellationToken.None);

        var listings = await store.GetListings(jobId, CancellationToken.None);
        var branded = listings.Single(l => l.ListingId == BrandedListingId);
        var unbranded = listings.Single(l => l.ListingId == UnbrandedListingId);

        Assert.Multiple(() =>
        {
            Assert.That(branded.Brand, Is.EqualTo("Nintendo"));
            Assert.That(unbranded.Brand, Is.Null);
        });
    }

    [Test]
    public async Task Should_add_the_brand_column_when_upgrading_a_database_created_before_the_brand_migration()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedPreBrandDatabaseAsync(factory);

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var columns = await ReadColumnNamesAsync(connection, "Listings");
        var legacy = await db.Listings.SingleAsync(l => l.ListingId == LegacyListingId);

        Assert.Multiple(() =>
        {
            Assert.That(columns, Does.Contain("Brand"));
            Assert.That(legacy.Title, Is.EqualTo("Legacy PS5"));
            Assert.That(legacy.Price, Is.EqualTo(199.99m));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private static ListingSummary BuildListing(string listingId, string? brand) =>
        new(
            ListingId: listingId,
            Title: "Console",
            Price: 100m,
            Currency: "USD",
            Url: $"https://www.mercari.com/us/item/{listingId}/",
            IsSold: false,
            Condition: "Used - Good",
            PrimaryImageUrl: "https://static.mercdn.net/console.jpg",
            BuyingFormat: null,
            Brand: brand);

    private static async Task SeedPreBrandDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreBrandMigration);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Listings (ListingId, ScrapeJobId, Title, Price, Currency, Url, IsSold, CreatedUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
            LegacyListingId,
            0,
            "Legacy PS5",
            199.99m,
            "GBP",
            "https://example.test/itm/legacy",
            true,
            DateTime.UtcNow);
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$table";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
