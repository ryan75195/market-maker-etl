using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MarketMakerEtl.Core.Data;

public static class DatabaseMigrationExtensions
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private static readonly string[] SchemaTables = ["ScrapeJobs", "ScrapeRuns", "Listings"];

    public static async Task ApplyMigrations(
        this IDbContextFactory<EtlDbContext> factory,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        if (await RequiresBaselineAsync(db, cancellationToken))
        {
            await ApplyBaselineAsync(db, cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<bool> RequiresBaselineAsync(
        EtlDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await OpenAsync(connection, cancellationToken);

        if (await TableExistsAsync(connection, MigrationsHistoryTable, cancellationToken))
        {
            return false;
        }

        foreach (var table in SchemaTables)
        {
            if (await TableExistsAsync(connection, table, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ApplyBaselineAsync(
        EtlDbContext db,
        CancellationToken cancellationToken)
    {
        var initialMigration = db.Database.GetMigrations().FirstOrDefault();
        if (initialMigration is null)
        {
            return;
        }

        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigration, ProductInfo.GetVersion())),
            cancellationToken);
    }

    private static async Task OpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }
}
