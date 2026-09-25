using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ListingClassificationStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-listing-classification-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_select_a_listing_that_has_never_been_classified()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var listingId = await SeedListing(jobId, "never-classified", null);

        var targets = await store.GetListingsNeedingClassification(jobId, taxonomyVersionId, 10, CancellationToken.None);

        Assert.That(targets.Select(t => t.ListingEntityId), Does.Contain(listingId));
    }

    [Test]
    public async Task Should_select_a_listing_whose_recorded_taxonomy_version_is_stale()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var oldVersionId = await SeedTaxonomyVersion();
        var newVersionId = await SeedTaxonomyVersion();
        var listingId = await SeedListing(jobId, "stale-version", null);
        await SeedClassificationRow(listingId, oldVersionId, "item_type", ClassificationSource.Model, DateTime.UtcNow);

        var targets = await store.GetListingsNeedingClassification(jobId, newVersionId, 10, CancellationToken.None);

        Assert.That(targets.Select(t => t.ListingEntityId), Does.Contain(listingId));
    }

    [Test]
    public async Task Should_select_a_listing_whose_description_arrived_after_classification()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var classifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var listingId = await SeedListing(jobId, "description-arrived-late", classifiedUtc.AddDays(1));
        await SeedClassificationRow(listingId, taxonomyVersionId, "item_type", ClassificationSource.Model, classifiedUtc);

        var targets = await store.GetListingsNeedingClassification(jobId, taxonomyVersionId, 10, CancellationToken.None);

        Assert.That(targets.Select(t => t.ListingEntityId), Does.Contain(listingId));
    }

    [Test]
    public async Task Should_skip_a_listing_that_is_up_to_date()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var classifiedUtc = DateTime.UtcNow;
        var listingId = await SeedListing(jobId, "up-to-date", classifiedUtc.AddDays(-1));
        await SeedClassificationRow(listingId, taxonomyVersionId, "item_type", ClassificationSource.Model, classifiedUtc);

        var targets = await store.GetListingsNeedingClassification(jobId, taxonomyVersionId, 10, CancellationToken.None);

        Assert.That(targets.Select(t => t.ListingEntityId), Does.Not.Contain(listingId));
    }

    [Test]
    public async Task Should_not_reselect_a_listing_whose_only_stale_row_is_human_owned()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var oldVersionId = await SeedTaxonomyVersion();
        var latestVersionId = await SeedTaxonomyVersion();
        var classifiedUtc = DateTime.UtcNow;
        var listingId = await SeedListing(jobId, "human-row-on-old-version", classifiedUtc.AddDays(-1));
        await SeedClassificationRow(listingId, latestVersionId, "item_type", ClassificationSource.Model, classifiedUtc);
        await SeedClassificationRow(listingId, oldVersionId, "colour", ClassificationSource.Human, classifiedUtc, "white");

        var targets = await store.GetListingsNeedingClassification(jobId, latestVersionId, 10, CancellationToken.None);

        Assert.That(targets.Select(t => t.ListingEntityId), Does.Not.Contain(listingId));
    }

    [Test]
    public async Task Should_return_human_choices_grouped_by_listing()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var humanListingId = await SeedListing(jobId, "human-choices", null);
        var modelOnlyListingId = await SeedListing(jobId, "model-only-choices", null);
        await SeedClassificationRow(humanListingId, taxonomyVersionId, "item_type", ClassificationSource.Human, DateTime.UtcNow, "console");
        await SeedClassificationRow(modelOnlyListingId, taxonomyVersionId, "item_type", ClassificationSource.Model, DateTime.UtcNow);

        var humanChoices = await store.GetHumanChoices([humanListingId, modelOnlyListingId], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(humanChoices.ContainsKey(modelOnlyListingId), Is.False);
            Assert.That(humanChoices[humanListingId]["item_type"], Is.EqualTo("console"));
        });
    }

    [Test]
    public async Task Should_not_overwrite_a_human_row_when_upserting_model_answers()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion();
        var listingId = await SeedListing(jobId, "human-reviewed", null);
        await SeedClassificationRow(listingId, taxonomyVersionId, "item_type", ClassificationSource.Human, DateTime.UtcNow, "console");

        await store.UpsertBatch(
            [
                new ListingClassificationBatchItem(
                    listingId,
                    taxonomyVersionId,
                    [new ListingClassificationRow("item_type", "dualsense_standard", "dualsense_standard", true, 0.9, 1.0, "{}")])
            ],
            CancellationToken.None);

        var classification = await store.GetClassification(listingId, CancellationToken.None);

        Assert.That(classification!.Answers.Single(a => a.Question == "item_type").Choice, Is.EqualTo("console"));
    }

    [Test]
    public async Task Should_upsert_and_read_back_classification_rows()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var taxonomyVersionId = await SeedTaxonomyVersion(version: 2);
        var listingId = await SeedListing(jobId, "fresh-upsert", null);

        await store.UpsertBatch(
            [
                new ListingClassificationBatchItem(
                    listingId,
                    taxonomyVersionId,
                    [
                        new ListingClassificationRow("item_type", "console", "console", true, 0.95, 1.0, "{\"console\":0.95}"),
                        new ListingClassificationRow("edition", "not_stated", null, false, 0.0, 0.0, "{}")
                    ])
            ],
            CancellationToken.None);

        var classification = await store.GetClassification(listingId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(classification, Is.Not.Null);
            Assert.That(classification!.TaxonomyVersion, Is.EqualTo(2));
            var itemType = classification.Answers.Single(a => a.Question == "item_type");
            Assert.That(itemType.Choice, Is.EqualTo("console"));
            Assert.That(itemType.ResolvedChoice, Is.EqualTo("console"));
            Assert.That(itemType.Confidence, Is.EqualTo(0.95));
            var edition = classification.Answers.Single(a => a.Question == "edition");
            Assert.That(edition.IsApplicable, Is.False);
            Assert.That(edition.ResolvedChoice, Is.Null);
        });
    }

    [Test]
    public async Task Should_return_null_when_the_listing_has_no_classification_rows()
    {
        var store = CreateStore();
        var jobId = await SeedJob();
        var listingId = await SeedListing(jobId, "unclassified", null);

        var classification = await store.GetClassification(listingId, CancellationToken.None);

        Assert.That(classification, Is.Null);
    }

    private ListingClassificationStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private async Task<int> SeedJob()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> SeedTaxonomyVersion(int version = 1)
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
            Version = version,
            QuestionsJson = "{}",
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();
        return taxonomyVersion.Id;
    }

    private async Task<int> SeedListing(int jobId, string listingId, DateTime? detailFetchedUtc)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Title = "Sony DualSense",
            DetailFetchedUtc = detailFetchedUtc,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private async Task SeedClassificationRow(
        int listingEntityId,
        int taxonomyVersionId,
        string question,
        ClassificationSource source,
        DateTime classifiedUtc,
        string choice = "console")
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
            Confidence = 0.9,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = source,
            ClassifiedUtc = classifiedUtc
        });
        await db.SaveChangesAsync();
    }
}
