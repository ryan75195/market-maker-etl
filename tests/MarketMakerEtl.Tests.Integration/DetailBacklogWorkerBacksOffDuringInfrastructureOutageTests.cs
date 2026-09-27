using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class DetailBacklogWorkerBacksOffDuringInfrastructureOutageTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 27, 2, 37, 0, TimeSpan.Zero);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-infra-outage-{Guid.NewGuid():N}.db");
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
    public async Task Should_leave_detail_fetch_attempts_untouched_and_back_off_across_a_simulated_proxy_outage()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var jobs = new JobStore(factory);
        var job = await jobs.CreateJob(
            new JobDetails("outage item", Marketplace.Mercari, null, 24, true, []), CancellationToken.None);
        var listingIds = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            listingIds.Add(await SeedListing(factory, job.Id, $"m0000outage{i}"));
        }

        var timeProvider = new FakeTimeProvider(StartTime);
        var backlogService = BuildBacklogService(jobs, factory, timeProvider);

        var firstOutageTick = await backlogService.RunTick(CancellationToken.None);
        var skippedByBackoffTick = await backlogService.RunTick(CancellationToken.None);
        var attemptsWhileBackingOff = await GetDetailFetchAttempts(factory, listingIds);

        timeProvider.Advance(TimeSpan.FromMinutes(30));
        var afterBackoffWindowTick = await backlogService.RunTick(CancellationToken.None);
        var attemptsAfterBackoffWindow = await GetDetailFetchAttempts(factory, listingIds);

        Assert.Multiple(() =>
        {
            Assert.That(firstOutageTick.Attempted, Is.GreaterThan(0));
            Assert.That(firstOutageTick.Succeeded, Is.EqualTo(0));
            Assert.That(skippedByBackoffTick.Selected, Is.EqualTo(0));
            Assert.That(afterBackoffWindowTick.Attempted, Is.GreaterThan(0));
            Assert.That(afterBackoffWindowTick.Succeeded, Is.EqualTo(0));
            Assert.That(attemptsWhileBackingOff, Is.All.EqualTo(0));
            Assert.That(attemptsAfterBackoffWindow, Is.All.EqualTo(0));
        });
    }

    private static DetailBacklogService BuildBacklogService(
        JobStore jobs, IDbContextFactory<EtlDbContext> factory, TimeProvider timeProvider)
    {
        var detailStore = new ItemDetailStore(factory);
        var client = new AlwaysInfrastructureUnavailableScrapeClient();
        var detailFetchOptions = new DetailFetchOptions(MaxConcurrentDetailFetches: 1, MaxDetailFetchesPerRun: 50, MaxDetailFetchAttempts: 3);
        var detailFetch = new ItemDetailFetchService(
            detailStore, client, [new MercariItemPageParser()], detailFetchOptions, NullLogger<ItemDetailFetchService>.Instance);
        var backlogOptions = new DetailBacklogOptions(
            Enabled: true,
            TickMinutes: 5,
            MaxFetchesPerTick: 10,
            MaxFetchesPerHour: 100,
            MaxDetailFetchAttempts: 3,
            MaxConcurrentDetailFetches: 1,
            InfrastructureBackoffBaseSeconds: 60,
            InfrastructureBackoffMaxSeconds: 1800);
        var backlog = new DetailBacklogStore(factory);
        var throttle = new DetailBacklogThrottleService(
            backlogOptions, timeProvider, NullLogger<DetailBacklogThrottleService>.Instance);
        return new DetailBacklogService(jobs, backlog, detailFetch, backlogOptions, throttle);
    }

    private static async Task<int> SeedListing(IDbContextFactory<EtlDbContext> factory, int jobId, string listingId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Url = $"https://www.mercari.com/us/item/{listingId}/",
            ItemStatus = "Active",
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private static async Task<IReadOnlyList<int>> GetDetailFetchAttempts(
        IDbContextFactory<EtlDbContext> factory, IReadOnlyCollection<int> listingEntityIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Listings
            .Where(l => listingEntityIds.Contains(l.Id))
            .Select(l => l.DetailFetchAttempts)
            .ToListAsync();
    }

    private sealed class AlwaysInfrastructureUnavailableScrapeClient : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            throw new FetchInfrastructureUnavailableException($"Simulated proxy outage fetching {url}.");
    }
}
