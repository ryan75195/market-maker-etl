using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class OpenAiUsageStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-openai-usage-store-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
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
    public async Task Should_record_usage_and_include_it_in_spend_since_the_given_time()
    {
        var store = CreateStore();
        var sinceUtc = DateTime.UtcNow.AddMinutes(-5);

        await store.Record("gpt-6-luna", OpenAiUsagePurpose.Classification, 100, 200, 0.5m, CancellationToken.None);

        var spend = await store.GetSpendSince(sinceUtc, CancellationToken.None);

        Assert.That(spend, Is.EqualTo(0.5m));
    }

    [Test]
    public async Task Should_sum_the_cost_of_multiple_recorded_records()
    {
        var store = CreateStore();
        var sinceUtc = DateTime.UtcNow.AddMinutes(-5);

        await store.Record("gpt-6-luna", OpenAiUsagePurpose.Classification, 100, 200, 0.5m, CancellationToken.None);
        await store.Record("gpt-6-sol", OpenAiUsagePurpose.OnboardingDraft, 50, 75, 1.25m, CancellationToken.None);

        var spend = await store.GetSpendSince(sinceUtc, CancellationToken.None);

        Assert.That(spend, Is.EqualTo(1.75m));
    }

    [Test]
    public async Task Should_exclude_usage_recorded_before_the_given_time()
    {
        var store = CreateStore();
        await store.Record("gpt-6-luna", OpenAiUsagePurpose.Classification, 100, 200, 0.5m, CancellationToken.None);
        var afterFirstRecord = DateTime.UtcNow.AddSeconds(1);

        var spend = await store.GetSpendSince(afterFirstRecord, CancellationToken.None);

        Assert.That(spend, Is.EqualTo(0m));
    }

    [Test]
    public async Task Should_return_zero_when_no_usage_has_been_recorded()
    {
        var store = CreateStore();

        var spend = await store.GetSpendSince(DateTime.UtcNow.AddDays(-30), CancellationToken.None);

        Assert.That(spend, Is.EqualTo(0m));
    }

    private OpenAiUsageStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
}
