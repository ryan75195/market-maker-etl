using Microsoft.Extensions.Time.Testing;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingRefreshSchedulingServiceTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-listing-refresh-scheduling-{Guid.NewGuid():N}.db");
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
    public async Task Should_refresh_listings_when_no_prior_refresh_is_recorded()
    {
        var refresh = Substitute.For<IListingRefreshService>();
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateService(refresh, timeProvider, refreshIntervalHours: 24);

        await scheduling.RefreshListingsIfDue(CancellationToken.None);

        await refresh.Received(1).RefreshActiveListings(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_not_refresh_listings_before_the_interval_elapses()
    {
        var refresh = Substitute.For<IListingRefreshService>();
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateService(refresh, timeProvider, refreshIntervalHours: 24);
        await scheduling.RefreshListingsIfDue(CancellationToken.None);
        refresh.ClearReceivedCalls();

        timeProvider.Advance(TimeSpan.FromHours(23));
        await scheduling.RefreshListingsIfDue(CancellationToken.None);

        await refresh.DidNotReceive().RefreshActiveListings(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_refresh_listings_again_once_the_interval_has_elapsed()
    {
        var refresh = Substitute.For<IListingRefreshService>();
        var timeProvider = new FakeTimeProvider(StartTime);
        var scheduling = CreateService(refresh, timeProvider, refreshIntervalHours: 24);
        await scheduling.RefreshListingsIfDue(CancellationToken.None);
        refresh.ClearReceivedCalls();

        timeProvider.Advance(TimeSpan.FromHours(24));
        await scheduling.RefreshListingsIfDue(CancellationToken.None);

        await refresh.Received(1).RefreshActiveListings(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_not_refresh_again_across_a_simulated_restart_within_the_interval()
    {
        var refresh = Substitute.For<IListingRefreshService>();
        var timeProvider = new FakeTimeProvider(StartTime);
        var firstInstance = CreateService(refresh, timeProvider, refreshIntervalHours: 24);
        await firstInstance.RefreshListingsIfDue(CancellationToken.None);
        refresh.ClearReceivedCalls();

        timeProvider.Advance(TimeSpan.FromHours(1));
        var secondInstance = CreateService(refresh, timeProvider, refreshIntervalHours: 24);
        await secondInstance.RefreshListingsIfDue(CancellationToken.None);

        await refresh.DidNotReceive().RefreshActiveListings(Arg.Any<CancellationToken>());
    }

    private ListingRefreshSchedulingService CreateService(
        IListingRefreshService refresh,
        TimeProvider timeProvider,
        int refreshIntervalHours)
    {
        var state = new SchedulerStateStore(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
        var options = new ScheduleOptions(TickMinutes: 5, RefreshIntervalHours: refreshIntervalHours);
        return new ListingRefreshSchedulingService(refresh, state, timeProvider, options);
    }
}
