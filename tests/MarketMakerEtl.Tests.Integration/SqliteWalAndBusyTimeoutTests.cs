using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class SqliteWalAndBusyTimeoutTests
{
    private const int ConfiguredBusyTimeoutMs = 5000;

    private string _databasePath = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-wal-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={_databasePath}",
                    ["Database:BusyTimeoutMs"] = ConfiguredBusyTimeoutMs.ToString(CultureInfo.InvariantCulture)
                });
            }));

        using var warmUpClient = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_report_wal_journal_mode_and_the_configured_busy_timeout()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();

        var journalMode = await QueryScalarAsync(connection, "PRAGMA journal_mode;");
        var busyTimeout = await QueryScalarAsync(connection, "PRAGMA busy_timeout;");

        Assert.Multiple(() =>
        {
            Assert.That(journalMode?.ToString(), Is.EqualTo("wal").IgnoreCase);
            Assert.That(Convert.ToInt32(busyTimeout, CultureInfo.InvariantCulture), Is.EqualTo(ConfiguredBusyTimeoutMs));
        });
    }

    [Test]
    public async Task Should_wait_for_a_concurrent_writer_instead_of_throwing_database_is_locked()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();

        await using var lockHoldingConnection = new SqliteConnection($"Data Source={_databasePath}");
        await lockHoldingConnection.OpenAsync();
        await ExecuteAsync(lockHoldingConnection, "BEGIN IMMEDIATE;");
        await ExecuteAsync(
            lockHoldingConnection,
            "INSERT INTO Categories (Name, IsEnabled, CreatedUtc) VALUES ('lock-holder', 1, '2026-01-01T00:00:00Z');");

        var waitingWriteTask = Task.Run(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            db.Categories.Add(new CategoryEntity
            {
                Name = "waiting-writer",
                IsEnabled = true,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await ExecuteAsync(lockHoldingConnection, "COMMIT;");

        Assert.That(async () => await waitingWriteTask, Throws.Nothing);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var names = await verifyDb.Categories.Select(c => c.Name).ToListAsync();
        Assert.That(names, Does.Contain("waiting-writer"));
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only fixed pragma/DDL statements, never built from external input.")]
    private static async Task<object?> QueryScalarAsync(System.Data.Common.DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync();
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only fixed pragma/DDL statements, never built from external input.")]
    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
