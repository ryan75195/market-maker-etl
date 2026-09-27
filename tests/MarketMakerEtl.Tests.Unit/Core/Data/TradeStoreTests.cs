using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Models.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class TradeStoreTests
{
    private static readonly PriceGroupOptions Options = new(SellerFeeRate: 0.10m, SellerFeeFixed: 0.30m, BuyerFeeRate: 0.036m);

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-trade-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_create_a_trade_and_default_buy_fees_from_price_group_options()
    {
        var store = CreateStore();
        var newTrade = new NewTrade(
            DealSignalId: null,
            ListingEntityId: null,
            ProductFamilyId: null,
            PriceGroupKey: new Dictionary<string, string> { ["model"] = "dualsense" },
            BoughtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BuyPrice: 100m,
            BuyShipping: null,
            BuyFees: null,
            Notes: "bought at flea market");

        var trade = await store.CreateTrade(newTrade, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(trade.Id, Is.GreaterThan(0));
            Assert.That(trade.BuyShipping, Is.EqualTo(0m));
            Assert.That(trade.BuyFees, Is.EqualTo(3.6m));
            Assert.That(trade.Status, Is.EqualTo(TradeStatus.Open));
            Assert.That(trade.PriceGroupKey!["model"], Is.EqualTo("dualsense"));
        });
    }

    [Test]
    public async Task Should_record_a_sale_and_compute_realised_profit_from_defaulted_fees()
    {
        var store = CreateStore();
        var created = await store.CreateTrade(BuildNewTrade(buyPrice: 60m, buyFees: 2m), CancellationToken.None);
        var sale = new TradeSale(
            SoldUtc: created.BoughtUtc.AddDays(5),
            SellPrice: 100m,
            SellShipping: null,
            SellFees: null,
            Status: null,
            Notes: null);

        var sold = await store.RecordSale(created.Id, sale, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sold, Is.Not.Null);
            Assert.That(sold!.Status, Is.EqualTo(TradeStatus.Sold));
            Assert.That(sold.SellFees, Is.EqualTo(10.30m));
            Assert.That(sold.RealisedProfit, Is.EqualTo(100m - 10.30m - (60m + 2m)));
            Assert.That(sold.DaysToSell, Is.EqualTo(5.0));
        });
    }

    [Test]
    public async Task Should_return_null_when_recording_a_sale_for_an_unknown_trade()
    {
        var store = CreateStore();

        var result = await store.RecordSale(
            999999,
            new TradeSale(DateTime.UtcNow, 10m, null, null, null, null),
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Should_list_trades_newest_first_via_get_trades()
    {
        var store = CreateStore();
        var first = await store.CreateTrade(BuildNewTrade(buyPrice: 10m), CancellationToken.None);
        var second = await store.CreateTrade(BuildNewTrade(buyPrice: 20m), CancellationToken.None);

        var trades = await store.GetTrades(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(trades, Has.Count.EqualTo(2));
            Assert.That(trades[0].Id, Is.EqualTo(second.Id));
            Assert.That(trades[1].Id, Is.EqualTo(first.Id));
        });
    }

    [Test]
    public async Task Should_get_a_single_trade_by_id_with_listing_details_populated()
    {
        var store = CreateStore();
        var listingId = await SeedListing();
        var created = await store.CreateTrade(BuildNewTrade(buyPrice: 30m, listingEntityId: listingId), CancellationToken.None);

        var fetched = await store.GetTrade(created.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.ListingTitle, Is.EqualTo("Test listing"));
            Assert.That(fetched.ListingUrl, Is.EqualTo("https://example.test/listing/1"));
        });
    }

    [Test]
    public async Task Should_get_null_for_an_unknown_trade_id()
    {
        var store = CreateStore();

        var fetched = await store.GetTrade(999999, CancellationToken.None);

        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task Should_summarise_realised_profit_and_predicted_margin_for_signal_linked_trades()
    {
        var store = CreateStore();
        var signalId = await SeedDealSignal(soldNetMedian: 100m, landedPrice: 70m);
        var linked = await store.CreateTrade(
            BuildNewTrade(buyPrice: 70m, buyFees: 0m, dealSignalId: signalId), CancellationToken.None);
        await store.RecordSale(
            linked.Id,
            new TradeSale(linked.BoughtUtc.AddDays(2), 110m, 0m, 0m, null, null),
            CancellationToken.None);

        var unlinked = await store.CreateTrade(BuildNewTrade(buyPrice: 40m, buyFees: 0m), CancellationToken.None);
        await store.RecordSale(
            unlinked.Id,
            new TradeSale(unlinked.BoughtUtc.AddDays(4), 60m, 0m, 0m, null, null),
            CancellationToken.None);

        var summary = await store.GetSummary(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Count, Is.EqualTo(2));
            Assert.That(summary.TotalRealisedProfit, Is.EqualTo(40m + 20m));
            Assert.That(summary.SignalComparison, Is.Not.Null);
            Assert.That(summary.SignalComparison!.EvaluatedCount, Is.EqualTo(1));
            Assert.That(summary.SignalComparison.MeanError, Is.EqualTo(40m - 30m));
            Assert.That(summary.SignalComparison.BeatPredictionShare, Is.EqualTo(1.0));
        });
    }

    private static NewTrade BuildNewTrade(
        decimal buyPrice, decimal? buyFees = null, int? listingEntityId = null, int? dealSignalId = null) => new(
        DealSignalId: dealSignalId,
        ListingEntityId: listingEntityId,
        ProductFamilyId: null,
        PriceGroupKey: null,
        BoughtUtc: DateTime.UtcNow,
        BuyPrice: buyPrice,
        BuyShipping: 0m,
        BuyFees: buyFees,
        Notes: null);

    private async Task<int> SeedListing()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = job.Id,
            Marketplace = Marketplace.Mercari,
            Title = "Test listing",
            Url = "https://example.test/listing/1",
            Currency = "USD",
            IsSold = false,
            Price = 30m,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return listing.Id;
    }

    private async Task<int> SeedDealSignal(decimal soldNetMedian, decimal landedPrice)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();

        var family = new ProductFamilyEntity
        {
            Key = $"trade-store-{Guid.NewGuid():N}",
            Name = "PS5 Controller",
            ModelName = "ps5-controller",
            CreatedUtc = DateTime.UtcNow
        };
        db.ProductFamilies.Add(family);
        await db.SaveChangesAsync();

        var taxonomyVersion = new TaxonomyVersionEntity
        {
            ProductFamilyId = family.Id,
            Version = 1,
            QuestionsJson = "{}",
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();

        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = job.Id,
            Marketplace = Marketplace.Mercari,
            Title = "Signal listing",
            Url = "https://example.test/listing/2",
            Currency = "USD",
            IsSold = false,
            Price = landedPrice,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var signal = new DealSignalEntity
        {
            ListingEntityId = listing.Id,
            ProductFamilyId = family.Id,
            TaxonomyVersionId = taxonomyVersion.Id,
            GroupKeyJson = """{"model":"dualsense"}""",
            LandedPrice = landedPrice,
            SoldNetMedian = soldNetMedian,
            SoldCount = 3,
            Discount = 0.20m,
            CreatedUtc = DateTime.UtcNow
        };
        db.DealSignals.Add(signal);
        await db.SaveChangesAsync();

        return signal.Id;
    }

    private TradeStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), Options);
}
