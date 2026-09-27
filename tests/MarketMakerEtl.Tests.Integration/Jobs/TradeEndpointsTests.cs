using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class TradeEndpointsTests : JobsApiTestBase
{
    [Test]
    public async Task Should_create_a_trade_and_default_buy_fees_from_price_group_options()
    {
        var request = new CreateTradeRequest(
            DealSignalId: null,
            ListingEntityId: null,
            ProductFamilyId: null,
            PriceGroupKey: null,
            BoughtUtc: DateTime.UtcNow,
            BuyPrice: 100m,
            BuyShipping: null,
            BuyFees: null,
            Notes: "bought from a friend");

        var response = await Client.PostAsJsonAsync("/api/trades", request);
        var trade = await response.Content.ReadFromJsonAsync<TradeView>(TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(trade, Is.Not.Null);
            Assert.That(trade!.BuyFees, Is.EqualTo(3.6m));
            Assert.That(trade.BuyShipping, Is.EqualTo(0m));
            Assert.That(trade.Status, Is.EqualTo(TradeStatus.Open));
        });
    }

    [Test]
    public async Task Should_record_a_sale_and_return_the_updated_trade_with_realised_profit()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/trades", new CreateTradeRequest(
            null, null, null, null, DateTime.UtcNow, 60m, 0m, 0m, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TradeView>(TestJsonOptions.Default);

        var saleRequest = new RecordTradeSaleRequest(
            SoldUtc: created!.BoughtUtc.AddDays(3),
            SellPrice: 100m,
            SellShipping: 0m,
            SellFees: 0m,
            Status: null,
            Notes: "shipped USPS");

        var saleResponse = await Client.PutAsJsonAsync($"/api/trades/{created.Id}/sale", saleRequest);
        var sold = await saleResponse.Content.ReadFromJsonAsync<TradeView>(TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(saleResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sold, Is.Not.Null);
            Assert.That(sold!.Status, Is.EqualTo(TradeStatus.Sold));
            Assert.That(sold.RealisedProfit, Is.EqualTo(40m));
            Assert.That(sold.DaysToSell, Is.EqualTo(3.0));
        });
    }

    [Test]
    public async Task Should_return_not_found_when_recording_a_sale_for_an_unknown_trade()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/trades/999999/sale",
            new RecordTradeSaleRequest(DateTime.UtcNow, 10m, null, null, null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Should_list_all_trades_via_get_trades()
    {
        await Client.PostAsJsonAsync("/api/trades", new CreateTradeRequest(
            null, null, null, null, DateTime.UtcNow, 10m, null, null, null));
        await Client.PostAsJsonAsync("/api/trades", new CreateTradeRequest(
            null, null, null, null, DateTime.UtcNow, 20m, null, null, null));

        var trades = await Client.GetFromJsonAsync<List<TradeView>>("/api/trades", TestJsonOptions.Default);

        Assert.That(trades, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Should_return_predicted_margin_vs_realised_profit_in_the_summary_for_a_signal_linked_trade()
    {
        var signalId = await SeedDealSignal(soldNetMedian: 100m, landedPrice: 70m);
        var createResponse = await Client.PostAsJsonAsync("/api/trades", new CreateTradeRequest(
            signalId, null, null, null, DateTime.UtcNow, 70m, 0m, 0m, null));
        var created = await createResponse.Content.ReadFromJsonAsync<TradeView>(TestJsonOptions.Default);

        await Client.PutAsJsonAsync(
            $"/api/trades/{created!.Id}/sale",
            new RecordTradeSaleRequest(created.BoughtUtc.AddDays(1), 110m, 0m, 0m, null, null));

        var summary = await Client.GetFromJsonAsync<TradeSummary>("/api/trades/summary", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.Count, Is.EqualTo(1));
            Assert.That(summary.TotalRealisedProfit, Is.EqualTo(40m));
            Assert.That(summary.MedianRealisedProfit, Is.EqualTo(40m));
            Assert.That(summary.SignalComparison, Is.Not.Null);
            Assert.That(summary.SignalComparison!.EvaluatedCount, Is.EqualTo(1));
            Assert.That(summary.SignalComparison.MeanError, Is.EqualTo(10m));
            Assert.That(summary.SignalComparison.BeatPredictionShare, Is.EqualTo(1.0));
        });
    }

    private async Task<int> SeedDealSignal(decimal soldNetMedian, decimal landedPrice)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var family = new ProductFamilyEntity
        {
            Key = $"trade-endpoints-{Guid.NewGuid():N}",
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
            Url = "https://example.test/listing/1",
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
}
