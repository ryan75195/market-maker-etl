using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class PriceGroupListingStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-price-group-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_return_no_candidates_when_no_questions_are_requested()
    {
        var store = CreateStore();

        var candidates = await store.GetCandidates(1, [], CancellationToken.None);

        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public async Task Should_build_a_candidate_from_a_listing_and_its_classification_rows()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(jobId, "listing-1", isSold: true, soldPrice: 150m, soldDaysAgo: 5);
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.95);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["colour"], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(candidates[0].ListingId, Is.EqualTo(listingId));
            Assert.That(candidates[0].SoldPrice, Is.EqualTo(150m));
            var answer = candidates[0].Answers.Single(a => a.Question == "colour");
            Assert.That(answer.ResolvedChoice, Is.EqualTo("white"));
            Assert.That(answer.NeedsReview, Is.False);
        });
    }

    [Test]
    public async Task Should_flag_a_low_confidence_model_answer_as_needing_review()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(jobId, "listing-2", isSold: false, soldPrice: null, soldDaysAgo: null);
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.5);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["colour"], CancellationToken.None);

        var answer = candidates.Single().Answers.Single(a => a.Question == "colour");
        Assert.That(answer.NeedsReview, Is.True);
    }

    [Test]
    public async Task Should_attach_a_normalized_mercari_condition_answer_to_every_candidate()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(
            jobId, "listing-3", isSold: false, soldPrice: null, soldDaysAgo: null, condition: "Like new");
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.95);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["colour"], CancellationToken.None);

        var answer = candidates.Single().Answers.Single(a => a.Question == "mercari_condition");
        Assert.Multiple(() =>
        {
            Assert.That(answer.ResolvedChoice, Is.EqualTo("like_new"));
            Assert.That(answer.IsApplicable, Is.True);
            Assert.That(answer.NeedsReview, Is.False);
        });
    }

    [Test]
    public async Task Should_mark_mercari_condition_as_not_applicable_when_the_condition_is_unrecognised()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(
            jobId, "listing-4", isSold: false, soldPrice: null, soldDaysAgo: null, condition: null);
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.95);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["colour"], CancellationToken.None);

        var answer = candidates.Single().Answers.Single(a => a.Question == "mercari_condition");
        Assert.That(answer.IsApplicable, Is.False);
    }

    [Test]
    public async Task Should_return_candidates_when_only_mercari_condition_is_requested()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(
            jobId, "listing-5", isSold: true, soldPrice: 90m, soldDaysAgo: 1, condition: "Good");
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.95);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["mercari_condition"], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(1));
            var answer = candidates.Single().Answers.Single(a => a.Question == "mercari_condition");
            Assert.That(answer.ResolvedChoice, Is.EqualTo("good"));
        });
    }

    [Test]
    public async Task Should_include_shipping_payer_and_cost_on_the_candidate()
    {
        var store = CreateStore();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var jobId = await SeedJob();
        var listingId = await SeedListing(
            jobId, "listing-6", isSold: true, soldPrice: 90m, soldDaysAgo: 1,
            shippingPayer: "seller", shippingCost: 6.5m);
        await SeedClassificationRow(listingId, taxonomyVersionId, "colour", "white", 0.95);

        var candidates = await store.GetCandidates(taxonomyVersionId, ["colour"], CancellationToken.None);

        var candidate = candidates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(candidate.ShippingPayer, Is.EqualTo("seller"));
            Assert.That(candidate.ShippingCost, Is.EqualTo(6.5m));
        });
    }

    private PriceGroupListingStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(), new ClassificationReviewOptions(0.9));

    private async Task<int> SeedJob()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> SeedTaxonomyVersion()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var family = new ProductFamilyEntity
        {
            Key = $"family-{Guid.NewGuid():N}",
            Name = "Family",
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
        return taxonomyVersion.Id;
    }

    private async Task<int> SeedListing(
        int jobId,
        string listingId,
        bool isSold,
        decimal? soldPrice,
        int? soldDaysAgo,
        string? condition = null,
        string? shippingPayer = null,
        decimal? shippingCost = null)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Title = "Sony DualSense",
            Currency = "USD",
            IsSold = isSold,
            SoldPrice = soldPrice,
            SoldDate = soldDaysAgo.HasValue ? DateTime.UtcNow.AddDays(-soldDaysAgo.Value) : null,
            Condition = condition,
            ShippingPayer = shippingPayer,
            ShippingCost = shippingCost,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private async Task SeedClassificationRow(
        int listingEntityId, int taxonomyVersionId, string question, string choice, double confidence)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingEntityId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = question,
            Choice = choice,
            ResolvedChoice = choice,
            IsApplicable = true,
            Confidence = confidence,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
