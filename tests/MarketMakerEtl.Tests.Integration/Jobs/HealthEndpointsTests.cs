using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class HealthEndpointsTests
{
    private string _databasePath = null!;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-health-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_report_ok_when_jobs_are_healthy()
    {
        var client = CreateClient();
        await CreateHealthyJob(client);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Ok));
            Assert.That(response.DatabaseReachable, Is.True);
            Assert.That(response.Jobs.Single().IsStale, Is.False);
            Assert.That(response.Fetcher.SidecarReachable, Is.True);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_fetcher_sidecar_is_unreachable()
    {
        var client = CreateClient(fetcherReachable: false);
        await CreateHealthyJob(client);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Fetcher.SidecarReachable, Is.False);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_recent_fetch_window_has_no_successes()
    {
        var client = CreateClient(fetcherReachable: true);
        await CreateHealthyJob(client);
        await SeedFetchOutcomes(FetchOutcomeKind.Infrastructure, 10);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Fetcher.RecentInfrastructureFailureCount, Is.EqualTo(10));
            Assert.That(response.Fetcher.RecentSuccessCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_report_degraded_when_an_enabled_job_has_no_completed_run()
    {
        var client = CreateClient();
        await CreateJob(client, "stale search", intervalHours: 1);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Jobs.Single().IsStale, Is.True);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_last_run_of_a_job_failed()
    {
        var client = CreateClient();
        var job = await CreateJob(client, "failed search", intervalHours: 24);
        await SeedRun(job.Id, ScrapeRunStatus.Failed, DateTime.UtcNow);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Jobs.Single().LastRunStatus, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(response.Jobs.Single().IsStale, Is.False);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_llm_is_degraded_and_a_family_exists()
    {
        var client = CreateClient();
        await CreateFamily(client, "ps5-controller");
        await SeedFailedBatchRuns(5);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Llm.Degraded, Is.True);
        });
    }

    [Test]
    public async Task Should_report_the_configured_model_and_whether_an_api_key_is_set()
    {
        var client = CreateClient(apiKey: "sk-test");

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response!.Llm.HasApiKey, Is.True);
            Assert.That(response.Llm.Model, Is.EqualTo("gpt-6-luna"));
        });
    }

    private HttpClient CreateClient(bool fetcherReachable = true, string? apiKey = null)
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                    };
                    if (apiKey is not null)
                    {
                        settings["OpenAI:ApiKey"] = apiKey;
                    }

                    configuration.AddInMemoryCollection(settings);
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IFetcherHealthClient>(new StubFetcherHealthClient(fetcherReachable));
                });
            });
        _client = _factory.CreateClient();
        return _client;
    }

    private static async Task<JobView> CreateJob(HttpClient client, string searchTerm, int intervalHours)
    {
        var response = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest(searchTerm, Marketplace.Mercari, null, intervalHours, true, []));
        return (await response.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default))!;
    }

    private async Task<JobView> CreateHealthyJob(HttpClient client)
    {
        var job = await CreateJob(client, "healthy search", 24);
        await SeedRun(job.Id, ScrapeRunStatus.Completed, DateTime.UtcNow);
        return job;
    }

    private static async Task<ProductFamilyView> CreateFamily(HttpClient client, string key)
    {
        var response = await client.PostAsJsonAsync(
            "/api/families", new CreateProductFamilyRequest(key, key, key));
        return (await response.Content.ReadFromJsonAsync<ProductFamilyView>())!;
    }

    private async Task SeedRun(int jobId, ScrapeRunStatus status, DateTime completedUtc)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.ScrapeRuns.Add(new ScrapeRunEntity
        {
            JobId = jobId,
            SearchTerm = "seed",
            Status = status.ToString(),
            TriggerType = nameof(TriggerType.Manual),
            StartedUtc = completedUtc.AddMinutes(-5),
            CompletedUtc = completedUtc
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedFetchOutcomes(FetchOutcomeKind kind, int count)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        await using var provider = services.BuildServiceProvider();
        var store = new FetchOutcomeStore(provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), TimeProvider.System);

        for (var attempt = 0; attempt < count; attempt++)
        {
            await store.RecordOutcome(kind, CancellationToken.None);
        }
    }

    private async Task SeedFailedBatchRuns(int count)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        for (var i = 0; i < count; i++)
        {
            db.ClassificationBatchRuns.Add(new ClassificationBatchRunEntity
            {
                RanUtc = DateTime.UtcNow,
                Succeeded = false
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class StubFetcherHealthClient : IFetcherHealthClient
    {
        private readonly bool _reachable;

        public StubFetcherHealthClient(bool reachable)
        {
            _reachable = reachable;
        }

        public Task<bool> CheckSidecarReachable(CancellationToken ct) => Task.FromResult(_reachable);
    }
}
