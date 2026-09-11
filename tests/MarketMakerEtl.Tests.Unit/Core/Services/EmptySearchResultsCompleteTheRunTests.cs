using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class EmptySearchResultsCompleteTheRunTests
{
    private const string GenuinelyEmptyPage = """
        <html>
          <body>
            <h1>No exact matches found</h1>
          </body>
        </html>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-empty-{Guid.NewGuid():N}.db");
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
    public async Task Should_complete_the_run_with_zero_listings_for_a_genuinely_empty_result_page()
    {
        var harness = BuildHarness();

        var jobId = await harness.Store.EnsureJob("ps5", CancellationToken.None);
        var runId = await harness.Store.EnqueueRun(jobId, "ps5", CancellationToken.None);
        var work = await harness.Store.ClaimNextQueuedRun(CancellationToken.None);

        await harness.Runs.Run(work!, CancellationToken.None);

        var run = await harness.Store.GetRun(runId, CancellationToken.None);
        var listings = await harness.Store.GetListings(jobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(run!.Status, Is.EqualTo(ScrapeRunStatus.Completed));
            Assert.That(listings, Is.Empty);
        });
    }

    private Harness BuildHarness()
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(GenuinelyEmptyPage);

        var urls = Substitute.For<IEbaySearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>()).Returns("https://search");

        var options = new ScrapeOptions(MaxPages: 1, CollectSold: false);
        var search = new SearchPageService(client, urls, new EbaySearchParser(), options);
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

        return new Harness(store, new ScrapeRunService(search, store));
    }

    private sealed record Harness(ScrapeStore Store, ScrapeRunService Runs);
}
