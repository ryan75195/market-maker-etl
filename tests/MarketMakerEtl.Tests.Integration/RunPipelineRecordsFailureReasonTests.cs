using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class RunPipelineRecordsFailureReasonTests
{
    private const string SearchTerm = "ps5";
    private const string FailureReason = "scrape client refused the connection";

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-{Guid.NewGuid():N}.db");
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
    public async Task Should_record_a_failed_run_carrying_the_scrape_failure_reason()
    {
        var store = CreateStore();
        var runs = CreateRunService(new FailingScrapeClient(FailureReason), store);

        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = (await store.GetRun(runId, CancellationToken.None))!;
        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recordedRun.Status, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(recordedRun.ErrorMessage, Is.EqualTo(FailureReason));
            Assert.That(listings, Is.Empty);
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private static ScrapeRunService CreateRunService(IScrapeClient client, ScrapeStore store) =>
        new(
            new SearchPageService(
                client,
                [new EbaySearchUrlService()],
                [new EbaySearchParser()],
                new ScrapeOptions(MaxPages: 1, CollectSold: false),
                NullLogger<SearchPageService>.Instance),
            store);

    private sealed class FailingScrapeClient(string reason) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            Task.FromException<string>(new InvalidOperationException(reason));
    }
}
