using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldListingsFromSoldSearchTests
{
    private const string SoldSearchPage = """
        <ul>
          <li class="s-item s-item__pl-on-bottom" data-view="mi:1686|iid:1">
            <a class="s-item__link" href="https://www.ebay.co.uk/itm/204512345678?hash=abc">
              <div class="s-item__title"><span role="heading">PlayStation 5 Disc Console</span></div>
            </a>
            <span class="s-item__price">£180.00</span>
            <span class="s-item__title--tagblock">Sold 12 Nov 2025</span>
          </li>
        </ul>
        """;

    private const string NoResultsPage = "<html><body><p>No exact matches found.</p></body></html>";

    private static readonly ListingSummary LegacyActiveListing =
        new("111111111111", "PlayStation 5 Legacy", 250m, "£", "https://www.ebay.co.uk/itm/111111111111", false);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-sold-{Guid.NewGuid():N}.db");
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
    public async Task Should_persist_a_listing_marked_sold_after_a_sold_search_run()
    {
        var harness = BuildHarness(collectSold: true);

        var jobId = await harness.Store.EnsureJob("ps5", CancellationToken.None);
        await harness.Store.UpsertListings(jobId, [LegacyActiveListing], CancellationToken.None);
        var runId = await harness.Store.EnqueueRun(jobId, "ps5", CancellationToken.None);
        var work = await harness.Store.ClaimNextQueuedRun(CancellationToken.None);

        await harness.Runs.Run(work!, CancellationToken.None);

        var listings = await harness.Store.GetListings(jobId, CancellationToken.None);
        var sold = listings.Where(l => l.IsSold).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sold.Select(l => l.ListingId), Is.EqualTo(new[] { "204512345678" }));
            Assert.That(sold.All(l => l.IsSold), Is.True);
            Assert.That(listings.Any(l => l.ListingId == LegacyActiveListing.ListingId && !l.IsSold), Is.True);
        });
    }

    private Harness BuildHarness(bool collectSold)
    {
        var client = Substitute.For<IScrapeClient>();
        client.GetPageHtml("https://active-search", Arg.Any<CancellationToken>()).Returns(NoResultsPage);
        client.GetPageHtml("https://sold-search", Arg.Any<CancellationToken>()).Returns(SoldSearchPage);

        var urls = Substitute.For<IEbaySearchUrlService>();
        urls.BuildSearch(Arg.Any<string>(), false, Arg.Any<int>()).Returns("https://active-search");
        urls.BuildSearch(Arg.Any<string>(), true, Arg.Any<int>()).Returns("https://sold-search");

        var options = new ScrapeOptions(MaxPages: 1, CollectSold: collectSold);
        var search = new SearchPageService(client, urls, new EbaySearchParser(), options);
        var store = new ScrapeStore(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());

        return new Harness(store, new ScrapeRunService(search, store));
    }

    private sealed record Harness(ScrapeStore Store, ScrapeRunService Runs);
}
