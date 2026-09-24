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
public class RunPipelineUsesInProcessScraperTests
{
    private const string SearchTerm = "ps5";

    private const string KnownSearchPage = """
        <ul>
          <li class="s-card" data-viewport="true">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?campid=1">Sony PlayStation 5 Console</a>
            <div class="s-card__title">Sony PlayStation 5 Console</div>
            <div class="s-card__price">$250.00</div>
          </li>
        </ul>
        """;

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
    public async Task Should_run_the_pipeline_through_an_in_process_scrape_client()
    {
        var urls = new EbaySearchUrlService();
        var expectedUrl = urls.BuildSearch(SearchTerm, sold: false, page: 1);
        var client = new InProcessScrapeClient(expectedUrl, KnownSearchPage);
        var store = CreateStore();
        var runs = CreateRunService(client, urls, store);

        var jobId = await store.EnsureJob(SearchTerm, CancellationToken.None);
        var runId = await store.EnqueueRun(jobId, SearchTerm, CancellationToken.None);
        var work = await store.ClaimNextQueuedRun(CancellationToken.None);

        await runs.Run(work!, CancellationToken.None);

        var recordedRun = (await store.GetRun(runId, CancellationToken.None))!;
        var listings = await store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.RequestedUrls, Is.EqualTo(new[] { expectedUrl }));
            Assert.That(recordedRun.Status, Is.EqualTo(ScrapeRunStatus.Completed));
            Assert.That(listings, Has.Count.EqualTo(1));
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

    private static ScrapeRunService CreateRunService(
        IScrapeClient client,
        IEbaySearchUrlService urls,
        ScrapeStore store) =>
        new(
            new SearchPageService(
                client,
                [urls],
                [new EbaySearchParser()],
                new ScrapeOptions(MaxPages: 1, CollectSold: false),
                NullLogger<SearchPageService>.Instance),
            store);

    private sealed class InProcessScrapeClient(string expectedUrl, string html) : IScrapeClient
    {
        private readonly List<string> _requestedUrls = [];

        public IReadOnlyList<string> RequestedUrls => _requestedUrls;

        public Task<string> GetPageHtml(string url, CancellationToken ct)
        {
            _requestedUrls.Add(url);
            return url == expectedUrl
                ? Task.FromResult(html)
                : Task.FromException<string>(
                    new InvalidOperationException($"unexpected outbound request to {url}"));
        }
    }
}
