using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class DealSignalServiceTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private IDealWebhookClient _webhook = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-deal-signal-service-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        _webhook = Substitute.For<IDealWebhookClient>();
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
    public async Task Should_create_a_signal_and_notify_the_webhook_for_a_qualifying_active_listing()
    {
        var familyId = await SeedHappyPathScenario();
        var service = CreateService();

        var result = await service.ScanForDeals(CancellationToken.None);
        var signals = await CreateSignalStore().GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.FamiliesScanned, Is.EqualTo(1));
            Assert.That(result.SignalsCreated, Is.EqualTo(1));
            Assert.That(signals, Has.Count.EqualTo(1));
            Assert.That(signals[0].LandedPrice, Is.EqualTo(70m));
            Assert.That(signals[0].Discount, Is.EqualTo(0.30m));
            Assert.That(signals[0].SoldNetMedian, Is.EqualTo(100m));
            Assert.That(signals[0].SoldCount, Is.EqualTo(3));
            Assert.That(signals[0].SoldP25, Is.EqualTo(95m));
            Assert.That(signals[0].GroupKey["model"], Is.EqualTo("dualsense"));
        });

        await _webhook.Received(1).Notify(
            Arg.Is<DealWebhookPayload>(p =>
                p.LandedPrice == 70m && p.Discount == 0.30m && p.SoldNetMedian == 100m),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_not_create_a_duplicate_signal_or_send_another_webhook_on_a_second_scan()
    {
        var familyId = await SeedHappyPathScenario();
        var service = CreateService();
        await service.ScanForDeals(CancellationToken.None);

        var secondResult = await service.ScanForDeals(CancellationToken.None);
        var signals = await CreateSignalStore().GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(secondResult.SignalsCreated, Is.EqualTo(0));
            Assert.That(signals, Has.Count.EqualTo(1));
        });
        await _webhook.Received(1).Notify(Arg.Any<DealWebhookPayload>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_exclude_listings_whose_group_key_answer_is_not_applicable_or_needs_review()
    {
        var familyId = await SeedReviewAndApplicabilityExclusionScenario();
        var service = CreateService();

        var result = await service.ScanForDeals(CancellationToken.None);
        var signals = await CreateSignalStore().GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.SignalsCreated, Is.EqualTo(0));
            Assert.That(signals, Is.Empty);
        });
    }

    [Test]
    public async Task Should_still_create_a_signal_when_the_webhook_fails()
    {
        var familyId = await SeedHappyPathScenario();
        _webhook.Notify(Arg.Any<DealWebhookPayload>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("webhook is down"));
        var service = CreateService();

        var result = await service.ScanForDeals(CancellationToken.None);
        var signals = await CreateSignalStore().GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.SignalsCreated, Is.EqualTo(1));
            Assert.That(signals, Has.Count.EqualTo(1));
        });
    }

    private async Task<int> SeedHappyPathScenario()
    {
        await using var db = await CreateDbContext();
        var family = await AddFamily(db, 0.20m, 2);
        var taxonomyVersionId = await AddTaxonomyVersion(db, family.Id);
        var jobId = await AddJob(db);

        await AddSoldListing(db, jobId, 90m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddSoldListing(db, jobId, 100m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddSoldListing(db, jobId, 110m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddSoldListing(db, jobId, 500m, taxonomyVersionId, isApplicable: false, confidence: 0.95);

        await AddActiveListing(db, jobId, 70m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddActiveListing(db, jobId, 95m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddActiveListing(db, jobId, 50m, taxonomyVersionId, isApplicable: true, confidence: 0.5);

        return family.Id;
    }

    private async Task<int> SeedReviewAndApplicabilityExclusionScenario()
    {
        await using var db = await CreateDbContext();
        var family = await AddFamily(db, 0.20m, 2);
        var taxonomyVersionId = await AddTaxonomyVersion(db, family.Id);
        var jobId = await AddJob(db);

        await AddSoldListing(db, jobId, 90m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddSoldListing(db, jobId, 100m, taxonomyVersionId, isApplicable: true, confidence: 0.95);
        await AddSoldListing(db, jobId, 110m, taxonomyVersionId, isApplicable: true, confidence: 0.95);

        await AddActiveListing(db, jobId, 40m, taxonomyVersionId, isApplicable: false, confidence: 0.95);
        await AddActiveListing(db, jobId, 45m, taxonomyVersionId, isApplicable: true, confidence: 0.5);

        return family.Id;
    }

    private async Task<ProductFamilyEntity> AddFamily(EtlDbContext db, decimal dealMinDiscount, int dealMinSold)
    {
        var family = new ProductFamilyEntity
        {
            Key = $"deal-signal-service-{Guid.NewGuid():N}",
            Name = "PS5 Controller",
            ModelName = "ps5-controller",
            DealGroupBy = "model",
            DealMinDiscount = dealMinDiscount,
            DealMinSold = dealMinSold,
            CreatedUtc = DateTime.UtcNow
        };
        db.ProductFamilies.Add(family);
        await db.SaveChangesAsync();
        return family;
    }

    private static async Task<int> AddTaxonomyVersion(EtlDbContext db, int familyId)
    {
        var taxonomyVersion = new TaxonomyVersionEntity
        {
            ProductFamilyId = familyId,
            Version = 1,
            QuestionsJson = "{}",
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();
        return taxonomyVersion.Id;
    }

    private static async Task<int> AddJob(EtlDbContext db)
    {
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> AddSoldListing(
        EtlDbContext db, int jobId, decimal soldPrice, int taxonomyVersionId, bool isApplicable, double confidence)
    {
        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Title = $"Sold listing {soldPrice}",
            Url = "https://example.test/sold",
            Currency = "USD",
            IsSold = true,
            SoldPrice = soldPrice,
            SoldDate = DateTime.UtcNow.AddDays(-1),
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        await AddClassification(db, listing.Id, taxonomyVersionId, isApplicable, confidence);
        return listing.Id;
    }

    private async Task<int> AddActiveListing(
        EtlDbContext db, int jobId, decimal price, int taxonomyVersionId, bool isApplicable, double confidence)
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
        await AddClassification(db, listing.Id, taxonomyVersionId, isApplicable, confidence);
        return listing.Id;
    }

    private static async Task AddClassification(
        EtlDbContext db, int listingId, int taxonomyVersionId, bool isApplicable, double confidence)
    {
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = "model",
            Choice = "dualsense",
            ResolvedChoice = "dualsense",
            IsApplicable = isApplicable,
            Confidence = confidence,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private Task<EtlDbContext> CreateDbContext() =>
        _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();

    private DealSignalService CreateService()
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        var reviewOptions = new ClassificationReviewOptions();
        var priceGroupOptions = new PriceGroupOptions(0m, 0m);
        var listingStore = new PriceGroupListingStore(factory, reviewOptions);
        var priceGroupQueryService = new PriceGroupQueryService(listingStore, TimeProvider.System, priceGroupOptions);
        var familyStore = new ProductFamilyStore(factory);
        return new DealSignalService(familyStore, priceGroupQueryService, CreateSignalStore(), _webhook);
    }

    private DealSignalStore CreateSignalStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
}
