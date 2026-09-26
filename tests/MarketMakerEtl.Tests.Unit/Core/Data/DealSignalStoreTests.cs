using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class DealSignalStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-deal-signal-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_insert_a_signal_and_make_it_visible_via_get_signals()
    {
        var store = CreateStore();
        var (familyId, taxonomyVersionId, listingId) = await SeedFamilyAndListing();
        var candidate = BuildCandidate(listingId, familyId, taxonomyVersionId, 80m);

        var inserted = await store.TryInsertSignal(candidate, CancellationToken.None);
        var signals = await store.GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.True);
            Assert.That(signals, Has.Count.EqualTo(1));
            Assert.That(signals[0].LandedPrice, Is.EqualTo(80m));
            Assert.That(signals[0].Discount, Is.EqualTo(0.20m));
            Assert.That(signals[0].GroupKey["model"], Is.EqualTo("dualsense"));
            Assert.That(signals[0].Title, Is.EqualTo("Test listing"));
        });
    }

    [Test]
    public async Task Should_not_insert_a_duplicate_signal_for_the_same_listing_and_landed_price()
    {
        var store = CreateStore();
        var (familyId, taxonomyVersionId, listingId) = await SeedFamilyAndListing();
        var candidate = BuildCandidate(listingId, familyId, taxonomyVersionId, 80m);
        await store.TryInsertSignal(candidate, CancellationToken.None);

        var insertedAgain = await store.TryInsertSignal(candidate, CancellationToken.None);
        var signals = await store.GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(insertedAgain, Is.False);
            Assert.That(signals, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_insert_a_new_signal_when_the_landed_price_changes()
    {
        var store = CreateStore();
        var (familyId, taxonomyVersionId, listingId) = await SeedFamilyAndListing();
        await store.TryInsertSignal(BuildCandidate(listingId, familyId, taxonomyVersionId, 80m), CancellationToken.None);

        var insertedAtLowerPrice = await store.TryInsertSignal(
            BuildCandidate(listingId, familyId, taxonomyVersionId, 70m), CancellationToken.None);
        var signals = await store.GetSignals(familyId, null, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(insertedAtLowerPrice, Is.True);
            Assert.That(signals, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_filter_signals_by_since_and_order_newest_first()
    {
        var store = CreateStore();
        var (familyId, taxonomyVersionId, listingOne) = await SeedFamilyAndListing();
        var listingTwo = await SeedListing();
        await store.TryInsertSignal(BuildCandidate(listingOne, familyId, taxonomyVersionId, 80m), CancellationToken.None);
        var cutoff = DateTime.UtcNow;
        await store.TryInsertSignal(BuildCandidate(listingTwo, familyId, taxonomyVersionId, 70m), CancellationToken.None);

        var signals = await store.GetSignals(familyId, cutoff, 50, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(signals, Has.Count.EqualTo(1));
            Assert.That(signals[0].LandedPrice, Is.EqualTo(70m));
        });
    }

    [Test]
    public async Task Should_summarise_performance_across_evaluated_signals_and_discount_buckets()
    {
        var store = CreateStore();
        var (familyId, taxonomyVersionId, listingId) = await SeedFamilyAndListing();
        await SeedEvaluatedSignal(familyId, taxonomyVersionId, listingId, 60m, 0.25m, margin: 10m, hours: 5);
        await SeedEvaluatedSignal(familyId, taxonomyVersionId, listingId, 50m, 0.55m, margin: -5m, hours: 15);
        await SeedUnevaluatedSignal(familyId, taxonomyVersionId, listingId, 40m, 0.60m);

        var performance = await store.GetPerformance(familyId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(performance.Overall.EvaluatedCount, Is.EqualTo(2));
            Assert.That(performance.Overall.PositiveMarginShare, Is.EqualTo(0.5));
            Assert.That(performance.Buckets.Single(b => b.Range == "20-30").Summary.EvaluatedCount, Is.EqualTo(1));
            Assert.That(performance.Buckets.Single(b => b.Range == "50+").Summary.EvaluatedCount, Is.EqualTo(1));
            Assert.That(performance.Buckets.Single(b => b.Range == "30-50").Summary.EvaluatedCount, Is.EqualTo(0));
        });
    }

    private async Task SeedEvaluatedSignal(
        int familyId, int taxonomyVersionId, int listingId, decimal landedPrice, decimal discount, decimal margin, double hours)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        db.DealSignals.Add(new DealSignalEntity
        {
            ListingEntityId = listingId,
            ProductFamilyId = familyId,
            TaxonomyVersionId = taxonomyVersionId,
            GroupKeyJson = """{"model":"dualsense"}""",
            LandedPrice = landedPrice,
            Discount = discount,
            CreatedUtc = DateTime.UtcNow.AddDays(-20),
            ForwardSoldCount = 4,
            ForwardNetMedian = landedPrice + margin,
            RealisedMargin = margin,
            ListingSoldWithinHours = hours
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedUnevaluatedSignal(
        int familyId, int taxonomyVersionId, int listingId, decimal landedPrice, decimal discount)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        db.DealSignals.Add(new DealSignalEntity
        {
            ListingEntityId = listingId,
            ProductFamilyId = familyId,
            TaxonomyVersionId = taxonomyVersionId,
            GroupKeyJson = """{"model":"dualsense"}""",
            LandedPrice = landedPrice,
            Discount = discount,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<SeededFamily> SeedFamilyAndListing()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();

        var family = new ProductFamilyEntity
        {
            Key = $"deal-signal-store-{Guid.NewGuid():N}",
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
            Title = "Test listing",
            Url = "https://example.test/listing/1",
            Currency = "USD",
            IsSold = false,
            Price = 80m,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return new SeededFamily(family.Id, taxonomyVersion.Id, listing.Id);
    }

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
            Title = "Second listing",
            Url = "https://example.test/listing/2",
            Currency = "USD",
            IsSold = false,
            Price = 70m,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return listing.Id;
    }

    private static DealSignalCandidate BuildCandidate(
        int listingId, int familyId, int taxonomyVersionId, decimal landedPrice) => new(
        listingId,
        familyId,
        taxonomyVersionId,
        new Dictionary<string, string> { ["model"] = "dualsense" },
        landedPrice,
        100m,
        6,
        90m,
        0.20m);

    private DealSignalStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private sealed record SeededFamily(int FamilyId, int TaxonomyVersionId, int ListingId);
}
