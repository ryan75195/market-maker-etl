using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MarketMakerEtl.Core.Data;

internal static class MigrationSchemaInspector
{
    public static async Task<bool> IsReflectedInSchema(
        DbConnection connection,
        Type migrationType,
        CancellationToken cancellationToken)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var operations = migration.UpOperations;
        if (operations.Count == 0)
        {
            return false;
        }

        foreach (var operation in operations)
        {
            if (!await IsOperationReflectedInSchema(connection, operation, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> IsOperationReflectedInSchema(
        DbConnection connection,
        MigrationOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case CreateTableOperation createTable:
                return await IsTableReflectedInSchema(connection, createTable, cancellationToken);
            case AddColumnOperation addColumn:
                return await ColumnExists(connection, addColumn.Table, addColumn.Name, cancellationToken);
            case CreateIndexOperation createIndex:
                return await IndexExists(connection, createIndex.Name, cancellationToken);
            default:
                return false;
        }
    }

    private static async Task<bool> IsTableReflectedInSchema(
        DbConnection connection,
        CreateTableOperation createTable,
        CancellationToken cancellationToken)
    {
        if (!await TableExists(connection, createTable.Name, cancellationToken))
        {
            return false;
        }

        foreach (var column in createTable.Columns)
        {
            if (!await ColumnExists(connection, createTable.Name, column.Name, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> TableExists(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        AddParameter(command, "$name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> ColumnExists(
        DbConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column";
        AddParameter(command, "$table", tableName);
        AddParameter(command, "$column", columnName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> IndexExists(
        DbConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name";
        AddParameter(command, "$name", indexName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
