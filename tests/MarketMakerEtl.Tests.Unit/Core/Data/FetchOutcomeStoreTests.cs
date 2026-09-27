using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class FetchOutcomeStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-fetch-outcomes-{Guid.NewGuid():N}.db");
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
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
    public async Task Should_return_zero_counts_when_no_outcomes_have_been_recorded()
    {
        var store = CreateStore();

        var snapshot = await store.GetRecentOutcomes(TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SuccessCount, Is.EqualTo(0));
            Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(0));
            Assert.That(snapshot.NotFoundFailureCount, Is.EqualTo(0));
            Assert.That(snapshot.OtherFailureCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_aggregate_repeated_outcomes_of_the_same_kind_within_a_minute()
    {
        var store = CreateStore();

        await store.RecordOutcome(FetchOutcomeKind.Success, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.Success, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.Success, CancellationToken.None);

        var snapshot = await store.GetRecentOutcomes(TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.That(snapshot.SuccessCount, Is.EqualTo(3));
    }

    [Test]
    public async Task Should_separate_counts_by_outcome_kind()
    {
        var store = CreateStore();

        await store.RecordOutcome(FetchOutcomeKind.Success, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.Infrastructure, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.Infrastructure, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.NotFound, CancellationToken.None);
        await store.RecordOutcome(FetchOutcomeKind.Other, CancellationToken.None);

        var snapshot = await store.GetRecentOutcomes(TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SuccessCount, Is.EqualTo(1));
            Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(2));
            Assert.That(snapshot.NotFoundFailureCount, Is.EqualTo(1));
            Assert.That(snapshot.OtherFailureCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_exclude_outcomes_recorded_before_the_requested_window()
    {
        var store = CreateStore();

        await store.RecordOutcome(FetchOutcomeKind.Success, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(90));
        await store.RecordOutcome(FetchOutcomeKind.Infrastructure, CancellationToken.None);

        var snapshot = await store.GetRecentOutcomes(TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SuccessCount, Is.EqualTo(0));
            Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_read_outcomes_recorded_by_a_different_store_instance_through_the_database()
    {
        var writerServices = new ServiceCollection();
        writerServices.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        await using var writerProvider = writerServices.BuildServiceProvider();
        var writerStore = new FetchOutcomeStore(
            writerProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), _timeProvider);

        var readerServices = new ServiceCollection();
        readerServices.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        await using var readerProvider = readerServices.BuildServiceProvider();
        var readerStore = new FetchOutcomeStore(
            readerProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), _timeProvider);

        await writerStore.RecordOutcome(FetchOutcomeKind.Infrastructure, CancellationToken.None);
        await writerStore.RecordOutcome(FetchOutcomeKind.Infrastructure, CancellationToken.None);

        var snapshot = await readerStore.GetRecentOutcomes(TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.That(snapshot.InfrastructureFailureCount, Is.EqualTo(2));
    }

    private FetchOutcomeStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), _timeProvider);
}
