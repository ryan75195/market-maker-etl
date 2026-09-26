using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using MarketMakerEtl.Etl.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Integration;

[TestFixture]
public class ClassificationPipelineTests
{
    private string _databasePath = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private ServiceProvider _directProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-it-classification-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                });
            }));
        _client = _factory.CreateClient();

        var directServices = new ServiceCollection();
        directServices.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _directProvider = directServices.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        _directProvider.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_classify_pending_listings_after_one_run_once_and_serve_them_through_the_read_endpoint()
    {
        _ = await _client.GetAsync("/health");
        var taxonomyJson = await File.ReadAllTextAsync(
            Path.Combine(FindSolutionRoot(), "docs", "taxonomies", "ps5-controller.json"));
        var family = await CreateFamily("ps5-controller");
        await PostTaxonomy(family.Id, taxonomyJson);
        var job = await CreateJob("ps5 controller");
        await SetJobFamily(job.Id, family.Id);
        var factory = _directProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var listingIds = await SeedListings(factory, job.Id);

        var worker = BuildWorker(factory);
        await worker.RunOnce(CancellationToken.None);

        var response = await _client.GetAsync($"/api/listings/{listingIds[0]}/classification");
        var classification = await response.Content.ReadFromJsonAsync<ListingClassificationView>(TestJsonOptions.Default);

        await using var db = await factory.CreateDbContextAsync();
        var storedRowCount = await db.ListingClassifications.CountAsync(
            c => listingIds.Contains(c.ListingEntityId));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(classification, Is.Not.Null);
            Assert.That(classification!.ListingEntityId, Is.EqualTo(listingIds[0]));
            Assert.That(classification.TaxonomyVersion, Is.EqualTo(1));
            Assert.That(classification.Answers, Has.Count.EqualTo(5));
            Assert.That(
                classification.Answers.Single(a => a.Question == "item_type").Choice,
                Is.EqualTo("dualsense_standard"));
            Assert.That(storedRowCount, Is.EqualTo(listingIds.Count * 5));
        });
    }

    [Test]
    public async Task Should_return_not_found_for_a_listing_with_no_classification()
    {
        var response = await _client.GetAsync("/api/listings/999999/classification");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<ProductFamilyView> CreateFamily(string key)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/families", new CreateProductFamilyRequest(key, key, key));
        return (await response.Content.ReadFromJsonAsync<ProductFamilyView>())!;
    }

    private async Task PostTaxonomy(int familyId, string questionsJson)
    {
        var content = new StringContent(questionsJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/api/families/{familyId}/taxonomies", content);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JobView> CreateJob(string searchTerm)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest(searchTerm, Marketplace.Mercari, null, 24, true, []));
        return (await response.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default))!;
    }

    private async Task SetJobFamily(int jobId, int familyId)
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/jobs/{jobId}/family", new SetJobFamilyRequest(familyId));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<int>> SeedListings(IDbContextFactory<EtlDbContext> factory, int jobId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var listings = new[]
        {
            new ListingEntity
            {
                ListingId = "m00000000001",
                ScrapeJobId = jobId,
                Title = "Sony DualSense controller white",
                Marketplace = Marketplace.Mercari,
                CreatedUtc = DateTime.UtcNow
            },
            new ListingEntity
            {
                ListingId = "m00000000002",
                ScrapeJobId = jobId,
                Title = "Sony DualSense controller black",
                Marketplace = Marketplace.Mercari,
                CreatedUtc = DateTime.UtcNow
            },
            new ListingEntity
            {
                ListingId = "m00000000003",
                ScrapeJobId = jobId,
                Title = "Sony DualSense controller red",
                Marketplace = Marketplace.Mercari,
                CreatedUtc = DateTime.UtcNow
            }
        };
        db.Listings.AddRange(listings);
        await db.SaveChangesAsync();
        return listings.Select(l => l.Id).ToList();
    }

    private static ClassificationWorker BuildWorker(IDbContextFactory<EtlDbContext> factory)
    {
        var families = new ProductFamilyStore(factory);
        var classifications = new ListingClassificationStore(factory);
        var options = new ClassifierOptions("http://classifier.test", 64, 5, 2000, 120);
        var service = new ListingClassificationService(
            families, classifications, new StubClassifierClient(), options);
        return new ClassificationWorker(service, options, TimeProvider.System, NullLogger<ClassificationWorker>.Instance);
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0)
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find solution root (no .slnx file found)");
    }

    private sealed class StubClassifierClient : IListingClassifierClient
    {
        public Task<ClassifyResponse> Classify(ClassifyRequest request, CancellationToken ct)
        {
            var answers = new Dictionary<string, ClassifyAnswer>
            {
                ["item_type"] = new("dualsense_standard", 0.9, 1.0, new Dictionary<string, double> { ["dualsense_standard"] = 0.9 }),
                ["edition"] = new("standard_colour", 0.9, 1.0, new Dictionary<string, double> { ["standard_colour"] = 0.9 }),
                ["colour"] = new("white", 0.9, 1.0, new Dictionary<string, double> { ["white"] = 0.9 }),
                ["quantity"] = new("one", 0.9, 1.0, new Dictionary<string, double> { ["one"] = 0.9 }),
                ["functional"] = new("working", 0.9, 1.0, new Dictionary<string, double> { ["working"] = 0.9 })
            };
            var results = request.States.Select(_ => new ClassifyResult(answers)).ToList();
            return Task.FromResult(new ClassifyResponse(request.Model, 3, results));
        }
    }
}
