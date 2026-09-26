using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class DealSignalBacktestStoreTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-deal-backtest-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_return_a_never_evaluated_signal_once_it_reaches_the_horizon()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-15), null, null);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_not_return_a_signal_before_it_reaches_the_horizon()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-5), null, null);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Is.Empty);
    }

    [Test]
    public async Task Should_not_return_a_signal_already_evaluated_with_enough_forward_sold_count()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-40), 14, 5);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Is.Empty);
    }

    [Test]
    public async Task Should_return_a_thin_signal_for_reevaluation_once_it_reaches_twice_the_horizon()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-28), 14, 1);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Should_not_return_a_thin_signal_before_it_reaches_twice_the_horizon()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-20), 14, 1);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Is.Empty);
    }

    [Test]
    public async Task Should_not_reevaluate_a_signal_that_already_used_the_extended_window()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        await SeedSignal(scenario, NowUtc.AddDays(-90), 28, 1);

        var pending = await store.GetSignalsPendingEvaluation(NowUtc, 14, 3, CancellationToken.None);

        Assert.That(pending, Is.Empty);
    }

    [Test]
    public async Task Should_report_the_sold_date_as_estimated_when_the_listing_has_no_real_sold_date()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        var listing = await UpdateListing(scenario.ListingId, isSold: true, soldDate: null);

        var saleInfo = await store.GetListingSaleInfo(scenario.ListingId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(saleInfo, Is.Not.Null);
            Assert.That(saleInfo!.IsSold, Is.True);
            Assert.That(saleInfo.SoldDateIsEstimated, Is.True);
        });
    }

    [Test]
    public async Task Should_report_the_sold_date_as_not_estimated_when_the_listing_has_a_real_sold_date()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        var soldDate = NowUtc.AddDays(-2);
        await UpdateListing(scenario.ListingId, isSold: true, soldDate: soldDate);

        var saleInfo = await store.GetListingSaleInfo(scenario.ListingId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(saleInfo!.SoldDateIsEstimated, Is.False);
            Assert.That(saleInfo.EffectiveSoldDateUtc, Is.EqualTo(soldDate));
        });
    }

    [Test]
    public async Task Should_return_null_sale_info_for_an_unknown_listing()
    {
        var store = CreateStore();

        var saleInfo = await store.GetListingSaleInfo(999_999, CancellationToken.None);

        Assert.That(saleInfo, Is.Null);
    }

    [Test]
    public async Task Should_apply_evaluation_results_to_matching_signals_only()
    {
        var store = CreateStore();
        var scenario = await SeedScenario();
        var signalId = await SeedSignal(scenario, NowUtc.AddDays(-15), null, null);
        var result = new DealSignalEvaluationResult(signalId, 120m, 4, 50m, 12.5, true, 14);

        await store.ApplyEvaluations([result, new DealSignalEvaluationResult(999_999, 1m, 1, 1m, 1, false, 14)],
            CancellationToken.None);

        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var updated = await db.DealSignals.SingleAsync(s => s.Id == signalId);
        Assert.Multiple(() =>
        {
            Assert.That(updated.ForwardNetMedian, Is.EqualTo(120m));
            Assert.That(updated.ForwardSoldCount, Is.EqualTo(4));
            Assert.That(updated.RealisedMargin, Is.EqualTo(50m));
            Assert.That(updated.ListingSoldWithinHours, Is.EqualTo(12.5));
            Assert.That(updated.UsedEstimatedDates, Is.True);
            Assert.That(updated.EvaluationWindowDays, Is.EqualTo(14));
        });
    }

    private async Task<SeededScenario> SeedScenario()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();

        var family = new ProductFamilyEntity
        {
            Key = $"deal-backtest-store-{Guid.NewGuid():N}",
            Name = "PS5 Controller",
            ModelName = "ps5-controller",
            CreatedUtc = NowUtc
        };
        db.ProductFamilies.Add(family);
        await db.SaveChangesAsync();

        var taxonomyVersion = new TaxonomyVersionEntity
        {
            ProductFamilyId = family.Id,
            Version = 1,
            QuestionsJson = "{}",
            CreatedUtc = NowUtc
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();

        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = NowUtc };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = job.Id,
            Marketplace = Marketplace.Mercari,
            Title = "Active listing",
            Url = "https://example.test/listing/1",
            Currency = "USD",
            IsSold = false,
            Price = 70m,
            CreatedUtc = NowUtc
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return new SeededScenario(family.Id, taxonomyVersion.Id, listing.Id);
    }

    private async Task<int> SeedSignal(
        SeededScenario scenario, DateTime createdUtc, int? evaluationWindowDays, int? forwardSoldCount)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var entity = new DealSignalEntity
        {
            ListingEntityId = scenario.ListingId,
            ProductFamilyId = scenario.FamilyId,
            TaxonomyVersionId = scenario.TaxonomyVersionId,
            GroupKeyJson = """{"model":"dualsense"}""",
            LandedPrice = 70m,
            Discount = 0.30m,
            CreatedUtc = createdUtc,
            EvaluationWindowDays = evaluationWindowDays,
            ForwardSoldCount = forwardSoldCount
        };
        db.DealSignals.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<ListingEntity> UpdateListing(int listingId, bool isSold, DateTime? soldDate)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var listing = await db.Listings.SingleAsync(l => l.Id == listingId);
        listing.IsSold = isSold;
        listing.SoldDate = soldDate;
        listing.SoldPrice = isSold ? 90m : null;
        await db.SaveChangesAsync();
        return listing;
    }

    private DealSignalBacktestStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private sealed record SeededScenario(int FamilyId, int TaxonomyVersionId, int ListingId);
}
