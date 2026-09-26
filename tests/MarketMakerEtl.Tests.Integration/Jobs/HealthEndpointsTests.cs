using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
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
    public async Task Should_report_ok_when_jobs_are_healthy_and_the_classifier_is_reachable()
    {
        var client = CreateClient(classifierReachable: true);
        await CreateHealthyJob(client);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health");

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Ok));
            Assert.That(response.DatabaseReachable, Is.True);
            Assert.That(response.Classifier.Reachable, Is.True);
            Assert.That(response.Jobs.Single().IsStale, Is.False);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_an_enabled_job_has_no_completed_run()
    {
        var client = CreateClient(classifierReachable: true);
        await CreateJob(client, "stale search", intervalHours: 1);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health");

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Jobs.Single().IsStale, Is.True);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_last_run_of_a_job_failed()
    {
        var client = CreateClient(classifierReachable: true);
        var job = await CreateJob(client, "failed search", intervalHours: 24);
        await SeedRun(job.Id, ScrapeRunStatus.Failed, DateTime.UtcNow);

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health");

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Jobs.Single().LastRunStatus, Is.EqualTo(ScrapeRunStatus.Failed));
            Assert.That(response.Jobs.Single().IsStale, Is.False);
        });
    }

    [Test]
    public async Task Should_report_degraded_when_the_classifier_is_unreachable_and_a_family_exists()
    {
        var client = CreateClient(classifierReachable: false);
        await CreateFamily(client, "ps5-controller");

        var response = await client.GetFromJsonAsync<SystemHealthResponse>("/api/health");

        Assert.Multiple(() =>
        {
            Assert.That(response!.Status, Is.EqualTo(SystemHealthStatus.Degraded));
            Assert.That(response.Classifier.Reachable, Is.False);
        });
    }

    private HttpClient CreateClient(bool classifierReachable)
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                    });
                });
                builder.ConfigureServices(services =>
                    services.AddSingleton<IListingClassifierClient>(new StubClassifierClient(classifierReachable)));
            });
        _client = _factory.CreateClient();
        return _client;
    }

    private static async Task<JobView> CreateJob(HttpClient client, string searchTerm, int intervalHours)
    {
        var response = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest(searchTerm, Marketplace.Mercari, null, intervalHours, true, []));
        return (await response.Content.ReadFromJsonAsync<JobView>())!;
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

    private sealed class StubClassifierClient : IListingClassifierClient
    {
        private readonly bool _reachable;

        public StubClassifierClient(bool reachable)
        {
            _reachable = reachable;
        }

        public Task<ClassifyResponse> Classify(ClassifyRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ClassifierHealthCheckResult> CheckHealth(CancellationToken ct) =>
            Task.FromResult(new ClassifierHealthCheckResult(
                "http://stub", _reachable, _reachable ? ["ps5-controller"] : []));
    }
}
