using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class MigrationsLeaveUpToDateDatabaseUnchangedTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-uptodate-{Guid.NewGuid():N}.db");
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
    public async Task Should_start_against_an_up_to_date_database_without_changing_the_schema()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await factory.ApplyMigrations(CancellationToken.None);

        IReadOnlyList<string> appliedBefore;
        IReadOnlyList<string> pendingBefore;
        await using (var db = await factory.CreateDbContextAsync())
        {
            appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            pendingBefore = (await db.Database.GetPendingMigrationsAsync()).ToList();
        }

        await factory.ApplyMigrations(CancellationToken.None);

        IReadOnlyList<string> appliedAfter;
        IReadOnlyList<string> pendingAfter;
        await using (var db = await factory.CreateDbContextAsync())
        {
            appliedAfter = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            pendingAfter = (await db.Database.GetPendingMigrationsAsync()).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(appliedBefore, Is.Not.Empty);
            Assert.That(pendingBefore, Is.Empty);
            Assert.That(appliedAfter, Is.EqualTo(appliedBefore));
            Assert.That(pendingAfter, Is.Empty);
        });
    }
}
