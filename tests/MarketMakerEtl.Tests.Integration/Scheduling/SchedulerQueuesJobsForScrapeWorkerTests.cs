using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using MarketMakerEtl.Etl.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Integration.Scheduling;

[TestFixture]
public class SchedulerQueuesJobsForScrapeWorkerTests
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

    private static readonly DateTimeOffset FixedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _databasePath = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-scheduler-it-{Guid.NewGuid():N}.db");
        _timeProvider = new FakeTimeProvider(FixedTime);
        var expectedUrl = new EbaySearchUrlService().BuildSearch(SearchTerm, sold: false, page: 1);
        var scrapeClient = new StubScrapeClient(expectedUrl, KnownSearchPage);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = $"Data Source={_databasePath}",
                        ["Scrape:MaxPages"] = "1",
                        ["Scrape:CollectSold"] = "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IScrapeClient>();
                    services.AddSingleton<IScrapeClient>(scrapeClient);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(_timeProvider);
                });
            });
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_queue_and_run_a_due_job_end_to_end()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest(SearchTerm, Marketplace.Ebay, IntervalHours: 1));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        using var scope = _factory.Services.CreateScope();
        var jobScheduling = scope.ServiceProvider.GetRequiredService<IJobSchedulingService>();
        var queuedCount = await jobScheduling.QueueDueJobs(CancellationToken.None);

        var scrapeStore = scope.ServiceProvider.GetRequiredService<IScrapeStore>();
        var scrapeRuns = scope.ServiceProvider.GetRequiredService<IScrapeRunService>();
        var worker = new ScrapeWorker(scrapeStore, scrapeRuns, NullLogger<ScrapeWorker>.Instance);
        var processed = await worker.RunOnce(CancellationToken.None);

        var jobAfterQueueing = await _client.GetFromJsonAsync<JobView>(
            $"/api/jobs/{created!.Id}", TestJsonOptions.Default);
        var listings = await _client.GetFromJsonAsync<List<ListingSummary>>(
            $"/api/scrape/jobs/{created.Id}/listings");

        Assert.Multiple(() =>
        {
            Assert.That(queuedCount, Is.EqualTo(1));
            Assert.That(processed, Is.True);
            Assert.That(jobAfterQueueing!.LastQueuedUtc, Is.EqualTo(FixedTime.UtcDateTime));
            Assert.That(listings, Has.Count.EqualTo(1));
            Assert.That(listings![0].Title, Is.EqualTo("Sony PlayStation 5 Console"));
        });
    }

    [Test]
    public async Task Should_not_requeue_the_job_before_its_interval_elapses()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest(SearchTerm, Marketplace.Ebay, IntervalHours: 24));
        await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        using (var firstScope = _factory.Services.CreateScope())
        {
            var jobScheduling = firstScope.ServiceProvider.GetRequiredService<IJobSchedulingService>();
            await jobScheduling.QueueDueJobs(CancellationToken.None);
        }

        _timeProvider.Advance(TimeSpan.FromHours(1));

        using var secondScope = _factory.Services.CreateScope();
        var secondJobScheduling = secondScope.ServiceProvider.GetRequiredService<IJobSchedulingService>();
        var secondQueuedCount = await secondJobScheduling.QueueDueJobs(CancellationToken.None);

        Assert.That(secondQueuedCount, Is.EqualTo(0));
    }

    private sealed class StubScrapeClient(string expectedUrl, string html) : IScrapeClient
    {
        public Task<string> GetPageHtml(string url, CancellationToken ct) =>
            url == expectedUrl
                ? Task.FromResult(html)
                : Task.FromResult("<html></html>");
    }
}
