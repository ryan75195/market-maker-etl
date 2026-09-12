using System.Data.Common;
using System.Globalization;
using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class LegacyDatabaseMigrationAddsStatusChangeTableTests
{
    private const string InitialMigration = "20260911124753_InitialCreate";
    private const string StatusMigration = "20260911221032_AddListingStatusAndSaleFields";
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-legacy-schema-{Guid.NewGuid():N}.db");
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
    public async Task Should_add_the_later_schema_objects_to_a_legacy_database_without_migration_history()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await CreateLegacyDatabaseAsync(factory);

        bool statusTableBefore;
        bool itemStatusColumnBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            statusTableBefore = await TableExistsAsync(connection, "ListingStatusChanges");
            itemStatusColumnBefore = (await ReadColumnNamesAsync(connection, "Listings")).Contains("ItemStatus");
        }

        await factory.ApplyMigrations(CancellationToken.None);

        bool statusTableAfter;
        HashSet<string> listingColumns;
        HashSet<string> statusChangeColumns;
        IReadOnlyList<string> applied;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            statusTableAfter = await TableExistsAsync(connection, "ListingStatusChanges");
            listingColumns = await ReadColumnNamesAsync(connection, "Listings");
            statusChangeColumns = await ReadColumnNamesAsync(connection, "ListingStatusChanges");
            applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(statusTableBefore, Is.False, "the legacy fixture must start without the status-change table");
            Assert.That(itemStatusColumnBefore, Is.False, "the legacy fixture must start without the status column");
            Assert.That(statusTableAfter, Is.True);
            Assert.That(statusChangeColumns, Does.Contain("Status"));
            Assert.That(statusChangeColumns, Does.Contain("ListingEntityId"));
            Assert.That(listingColumns, Does.Contain("ItemStatus"));
            Assert.That(listingColumns, Does.Contain("SoldPrice"));
            Assert.That(listingColumns, Does.Contain("SoldDate"));
            Assert.That(listingColumns, Does.Contain("Seller"));
            Assert.That(listingColumns, Does.Contain("Condition"));
            Assert.That(listingColumns, Does.Contain("PrimaryImageUrl"));
            Assert.That(listingColumns, Does.Contain("BuyingFormat"));
            Assert.That(applied, Does.Contain(StatusMigration));
        });
    }

    private static async Task CreateLegacyDatabaseAsync(IDbContextFactory<EtlDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE " + MigrationsHistoryTable);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
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
