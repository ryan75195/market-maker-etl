using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MarketMakerEtl.Core.Data;

internal sealed class SqlitePragmaConnectionInterceptor(int busyTimeoutMs) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = BuildPragmaCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = BuildPragmaCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "PRAGMA statements take only a literal integer configured by the app, never user input, and SQLite does not support bound parameters inside a PRAGMA.")]
    private DbCommand BuildPragmaCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = BuildPragmaScript(connection);
        return command;
    }

    internal string BuildPragmaScript(DbConnection connection)
    {
        var busyTimeoutPragma = string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA busy_timeout = {busyTimeoutMs};");

        return CanEnableWal(connection)
            ? $"{busyTimeoutPragma} PRAGMA journal_mode = 'WAL';"
            : busyTimeoutPragma;
    }

    private static bool CanEnableWal(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return false;
        }

        var builder = new SqliteConnectionStringBuilder(sqliteConnection.ConnectionString);
        return builder.Mode is not (SqliteOpenMode.Memory or SqliteOpenMode.ReadOnly)
            && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
    }
}
