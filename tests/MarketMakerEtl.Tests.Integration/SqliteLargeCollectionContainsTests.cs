using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class SqliteLargeCollectionContainsTests
{
    private const int IdsBeyondSqliteVariableLimit = 40_000;

    private string _databasePath = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-contains-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                })));

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
    public async Task Should_match_rows_when_filtering_by_more_ids_than_sqlite_allows_variables()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await SeedCategories(factory, "first", "second", "third");

        await using var db = await factory.CreateDbContextAsync();
        var knownIds = await db.Categories.Where(c => c.Name != "third").Select(c => c.Id).ToListAsync();
        var ids = Enumerable.Range(1_000_000, IdsBeyondSqliteVariableLimit).Concat(knownIds).ToList();

        var names = await db.Categories.Where(c => ids.Contains(c.Id)).Select(c => c.Name).ToListAsync();

        Assert.That(names, Is.EquivalentTo(new[] { "first", "second" }));
    }

    private static async Task SeedCategories(IDbContextFactory<EtlDbContext> factory, params string[] names)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var name in names)
        {
            db.Categories.Add(new CategoryEntity { Name = name, IsEnabled = true, CreatedUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
    }
}
