using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class DealSignalEndpointsTests : JobsApiTestBase
{
    private const string TaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "model": {
           "instructions": "Which model?",
           "criteria": { "dualsense": "DualSense." }
          }
         }
        }
        """;

    [Test]
    public async Task Should_put_deal_settings_then_surface_signals_from_a_scan_newest_first()
    {
        var family = await CreateFamily("ps5-controller-deals");
        var putResponse = await Client.PutAsJsonAsync(
            $"/api/families/{family.Id}",
            new UpdateProductFamilyRequest(null, null, "model", 0.20m, 2));
        await SeedTaxonomyAndListings(family.Id);

        var scanResult = await RunDealScan();
        var getResponse = await Client.GetAsync($"/api/families/{family.Id}/deals");
        var deals = await getResponse.Content.ReadFromJsonAsync<List<DealSignalView>>();

        var expectedSoldNetMedian = MedianSoldNetProceeds();
        var expectedDiscount = DealDiscountCalculator.Calculate(expectedSoldNetMedian, 70m);
        Assert.Multiple(() =>
        {
            Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(scanResult.SignalsCreated, Is.EqualTo(1));
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deals, Has.Count.EqualTo(1));
            Assert.That(deals![0].LandedPrice, Is.EqualTo(70m));
            Assert.That(deals[0].SoldNetMedian, Is.EqualTo(expectedSoldNetMedian));
            Assert.That(deals[0].Discount, Is.EqualTo(expectedDiscount));
            Assert.That(deals[0].GroupKey["model"], Is.EqualTo("dualsense"));
        });
    }

    private static decimal MedianSoldNetProceeds()
    {
        var options = PriceGroupOptionsFactory.Build(null);
        var netProceeds = new[] { 90m, 100m, 110m }
            .Select(soldPrice => PriceGroupNetCalculator.ComputeNetProceeds(soldPrice, null, null, options))
            .OrderBy(value => value)
            .ToList();
        return netProceeds[1];
    }

    [Test]
    public async Task Should_return_newest_signals_first_and_respect_since()
    {
        var family = await CreateFamily("ps5-controller-deals-ordering");
        await Client.PutAsJsonAsync(
            $"/api/families/{family.Id}",
            new UpdateProductFamilyRequest(null, null, "model", 0.20m, 2));
        await SeedTaxonomyAndListings(family.Id);
        await RunDealScan();
        var afterFirstScanUtc = DateTime.UtcNow;
        await AddCheaperActiveListing(family.Id);
        await RunDealScan();

        var allDeals = await Client.GetFromJsonAsync<List<DealSignalView>>($"/api/families/{family.Id}/deals");
        var recentDeals = await Client.GetFromJsonAsync<List<DealSignalView>>(
            $"/api/families/{family.Id}/deals?since={Uri.EscapeDataString(afterFirstScanUtc.ToString("O"))}");

        Assert.Multiple(() =>
        {
            Assert.That(allDeals, Has.Count.EqualTo(2));
            Assert.That(allDeals![0].LandedPrice, Is.EqualTo(40m));
            Assert.That(allDeals[1].LandedPrice, Is.EqualTo(70m));
            Assert.That(recentDeals, Has.Count.EqualTo(1));
            Assert.That(recentDeals![0].LandedPrice, Is.EqualTo(40m));
        });
    }

    [Test]
    public async Task Should_return_not_found_for_deals_on_an_unknown_family()
    {
        var response = await Client.GetAsync("/api/families/999999/deals");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Should_evaluate_a_backtest_signal_and_return_the_expected_performance_json()
    {
        var family = await CreateFamily("ps5-controller-deals-performance");
        await Client.PutAsJsonAsync(
            $"/api/families/{family.Id}",
            new UpdateProductFamilyRequest(null, null, "model", 0.20m, 2));
        var seeded = await SeedTaxonomyAndListings(family.Id);
        var createdUtc = DateTime.UtcNow.AddDays(-20);

        var flaggedListingId = await GetListingIdByPrice(seeded.JobId, 70m);
        await AddSoldListingWithDate(seeded.JobId, seeded.TaxonomyVersionId, 90m, createdUtc.AddDays(2));
        await AddSoldListingWithDate(seeded.JobId, seeded.TaxonomyVersionId, 100m, createdUtc.AddDays(4));
        await AddSoldListingWithDate(seeded.JobId, seeded.TaxonomyVersionId, 110m, createdUtc.AddDays(6));
        await SeedBacktestSignal(
            family.Id, seeded.TaxonomyVersionId, flaggedListingId, createdUtc, discount: 0.25m, landedPrice: 70m);

        await RunDealBacktest();
        var performance = await Client.GetFromJsonAsync<DealPerformanceReport>(
            $"/api/families/{family.Id}/deals/performance");

        var expectedNetMedian = MedianSoldNetProceeds();
        var expectedMargin = expectedNetMedian - 70m;
        Assert.Multiple(() =>
        {
            Assert.That(performance, Is.Not.Null);
            Assert.That(performance!.Overall.EvaluatedCount, Is.EqualTo(1));
            Assert.That(performance.Overall.PositiveMarginShare, Is.EqualTo(1.0));
            Assert.That(performance.Overall.MedianMargin, Is.EqualTo(expectedMargin));
            Assert.That(performance.Overall.MedianListingSoldWithinHours, Is.Null);
            Assert.That(performance.Buckets.Single(b => b.Range == "20-30").Summary.EvaluatedCount, Is.EqualTo(1));
            Assert.That(performance.Buckets.Single(b => b.Range == "20-30").Summary.MedianMargin, Is.EqualTo(expectedMargin));
            Assert.That(performance.Buckets.Single(b => b.Range == "30-50").Summary.EvaluatedCount, Is.EqualTo(0));
            Assert.That(performance.Buckets.Single(b => b.Range == "50+").Summary.EvaluatedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_return_not_found_for_deals_performance_on_an_unknown_family()
    {
        var response = await Client.GetAsync("/api/families/999999/deals/performance");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<DealScanTickResult> RunDealScan()
    {
        using var scope = Factory.Services.CreateScope();
        var deals = scope.ServiceProvider.GetRequiredService<IDealSignalService>();
        return await deals.ScanForDeals(CancellationToken.None);
    }

    private async Task<DealBacktestTickResult> RunDealBacktest()
    {
        using var scope = Factory.Services.CreateScope();
        var backtest = scope.ServiceProvider.GetRequiredService<IDealSignalBacktestService>();
        return await backtest.EvaluateSignals(CancellationToken.None);
    }

    private async Task<ProductFamilyView> CreateFamily(string key)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest(key, key, key));
        return (await response.Content.ReadFromJsonAsync<ProductFamilyView>())!;
    }

    private async Task<SeededTaxonomy> SeedTaxonomyAndListings(int familyId)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var taxonomyVersion = new TaxonomyVersionEntity
        {
            ProductFamilyId = familyId,
            Version = 1,
            QuestionsJson = TaxonomyJson,
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();

        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        await AddSoldListing(db, job.Id, taxonomyVersion.Id, 90m);
        await AddSoldListing(db, job.Id, taxonomyVersion.Id, 100m);
        await AddSoldListing(db, job.Id, taxonomyVersion.Id, 110m);
        await AddActiveListing(db, job.Id, taxonomyVersion.Id, 70m);
        await AddActiveListing(db, job.Id, taxonomyVersion.Id, 95m);

        return new SeededTaxonomy(taxonomyVersion.Id, job.Id);
    }

    private async Task AddCheaperActiveListing(int familyId)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var taxonomyVersion = await db.TaxonomyVersions
            .Where(v => v.ProductFamilyId == familyId)
            .OrderByDescending(v => v.Version)
            .FirstAsync();
        var job = await db.ScrapeJobs.FirstAsync();

        await AddActiveListing(db, job.Id, taxonomyVersion.Id, 40m);
    }

    private static async Task AddSoldListing(EtlDbContext db, int jobId, int taxonomyVersionId, decimal soldPrice)
    {
        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Title = $"Sold listing {soldPrice}",
            Currency = "USD",
            IsSold = true,
            SoldPrice = soldPrice,
            SoldDate = DateTime.UtcNow.AddDays(-1),
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        await AddClassification(db, listing.Id, taxonomyVersionId);
    }

    private async Task AddSoldListingWithDate(int jobId, int taxonomyVersionId, decimal soldPrice, DateTime soldDate)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Title = $"Forward sold listing {soldPrice}",
            Currency = "USD",
            IsSold = true,
            SoldPrice = soldPrice,
            SoldDate = soldDate,
            CreatedUtc = soldDate
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        await AddClassification(db, listing.Id, taxonomyVersionId);
    }

    private async Task<int> GetListingIdByPrice(int jobId, decimal price)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Listings
            .Where(l => l.ScrapeJobId == jobId && l.Price == price)
            .Select(l => l.Id)
            .SingleAsync();
    }

    private async Task SeedBacktestSignal(
        int familyId, int taxonomyVersionId, int listingId, DateTime createdUtc, decimal discount, decimal landedPrice)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.DealSignals.Add(new DealSignalEntity
        {
            ListingEntityId = listingId,
            ProductFamilyId = familyId,
            TaxonomyVersionId = taxonomyVersionId,
            GroupKeyJson = """{"model":"dualsense"}""",
            LandedPrice = landedPrice,
            Discount = discount,
            CreatedUtc = createdUtc
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddActiveListing(EtlDbContext db, int jobId, int taxonomyVersionId, decimal price)
    {
        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Title = $"Active listing {price}",
            Url = "https://example.test/active",
            Currency = "USD",
            IsSold = false,
            Price = price,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        await AddClassification(db, listing.Id, taxonomyVersionId);
    }

    private static async Task AddClassification(EtlDbContext db, int listingId, int taxonomyVersionId)
    {
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = "model",
            Choice = "dualsense",
            ResolvedChoice = "dualsense",
            IsApplicable = true,
            Confidence = 0.95,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed record SeededTaxonomy(int TaxonomyVersionId, int JobId);
}
