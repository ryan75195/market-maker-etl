using System.Data.Common;
using System.Globalization;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigratedListingSchemaIncludesStatusSaleFieldsAndHistoryTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-listing-schema-{Guid.NewGuid():N}.db");
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
    public async Task Should_include_status_sale_fields_and_history_storage_after_migrating_an_empty_database()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await factory.ApplyMigrations(CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var listingEntity = db.Model.FindEntityType(typeof(ListingEntity))!;
        var historyEntity = db.Model.FindEntityType(typeof(ListingStatusChangeEntity))!;
        var expectedListingColumns = ColumnsByProperty(listingEntity);
        var expectedHistoryColumns = ColumnsByProperty(historyEntity);

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var storedListingColumns = await ReadColumnNames(connection, listingEntity.GetTableName()!);
        var historyTableExists = await TableExists(connection, historyEntity.GetTableName()!);
        var storedHistoryColumns = historyTableExists
            ? await ReadColumnNames(connection, historyEntity.GetTableName()!)
            : [];

        Assert.Multiple(() =>
        {
            Assert.That(storedListingColumns, Does.Contain(expectedListingColumns["ItemStatus"]));
            Assert.That(storedListingColumns, Does.Contain(expectedListingColumns["SoldPrice"]));
            Assert.That(storedListingColumns, Does.Contain(expectedListingColumns["SoldDate"]));
            Assert.That(storedListingColumns, Does.Contain(expectedListingColumns["Seller"]));
            Assert.That(historyTableExists, Is.True);
            Assert.That(storedHistoryColumns, Does.Contain(expectedHistoryColumns["Status"]));
            Assert.That(storedHistoryColumns, Does.Contain(expectedHistoryColumns["ListingEntityId"]));
        });
    }

    private static Dictionary<string, string> ColumnsByProperty(IEntityType entityType) =>
        entityType.GetProperties().ToDictionary(
            property => property.Name,
            property => property.GetColumnName(),
            StringComparer.Ordinal);

    private static async Task<HashSet<string>> ReadColumnNames(DbConnection connection, string tableName)
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

    private static async Task<bool> TableExists(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        return count > 0;
    }
}
