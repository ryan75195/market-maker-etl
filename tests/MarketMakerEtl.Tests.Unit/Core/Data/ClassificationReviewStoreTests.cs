using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ClassificationReviewStoreTests
{
    private const string GatedTaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": { "console": "A console.", "dualsense_standard": "A DualSense controller." }
          },
          "edition": {
           "instructions": "Which edition?",
           "criteria": { "standard_colour": "Standard.", "limited_edition": "Limited." },
           "askWhen": [ { "question": "item_type", "anyOf": ["dualsense_standard"] } ]
          },
          "colour": {
           "instructions": "Which colour?",
           "criteria": { "white": "White.", "midnight_black": "Black." },
           "askWhen": [ { "question": "item_type", "anyOf": ["dualsense_standard"] } ]
          }
         }
        }
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-review-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_order_the_review_queue_by_confidence_ascending_and_respect_take()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var lowest = await SeedListing(jobId, "m-low");
        var middle = await SeedListing(jobId, "m-mid");
        var highest = await SeedListing(jobId, "m-high");
        await SeedRow(lowest, versionId, "item_type", ClassificationSource.Model, confidence: 0.5);
        await SeedRow(middle, versionId, "item_type", ClassificationSource.Model, confidence: 0.7);
        await SeedRow(highest, versionId, "item_type", ClassificationSource.Model, confidence: 0.8);

        var queue = await store.GetReviewQueue(familyId, versionId, questions: null, threshold: 0.9, take: 2, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(queue, Has.Count.EqualTo(2));
            Assert.That(queue[0].ListingEntityId, Is.EqualTo(lowest));
            Assert.That(queue[1].ListingEntityId, Is.EqualTo(middle));
        });
    }

    [Test]
    public async Task Should_exclude_human_and_non_applicable_rows_from_the_review_queue()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var humanListing = await SeedListing(jobId, "m-human");
        var notApplicableListing = await SeedListing(jobId, "m-not-applicable");
        await SeedRow(humanListing, versionId, "item_type", ClassificationSource.Human, confidence: 0.5);
        await SeedRow(notApplicableListing, versionId, "item_type", ClassificationSource.Model, confidence: 0.5, isApplicable: false);

        var queue = await store.GetReviewQueue(familyId, versionId, questions: null, threshold: 0.9, take: 50, CancellationToken.None);

        Assert.That(queue, Is.Empty);
    }

    [Test]
    public async Task Should_filter_the_review_queue_by_question()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-multi");
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.5);
        await SeedRow(listing, versionId, "edition", ClassificationSource.Model, confidence: 0.5);

        var queue = await store.GetReviewQueue(familyId, versionId, questions: ["edition"], threshold: 0.9, take: 50, CancellationToken.None);

        Assert.That(queue.Single().Question, Is.EqualTo("edition"));
    }

    [Test]
    public async Task Should_filter_the_review_queue_by_multiple_questions()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-multi-question");
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.5);
        await SeedRow(listing, versionId, "edition", ClassificationSource.Model, confidence: 0.5);
        await SeedRow(listing, versionId, "colour", ClassificationSource.Model, confidence: 0.5);

        var queue = await store.GetReviewQueue(
            familyId, versionId, questions: ["item_type", "colour"], threshold: 0.9, take: 50, CancellationToken.None);

        Assert.That(queue.Select(row => row.Question), Is.EquivalentTo(new[] { "item_type", "colour" }));
    }

    [Test]
    public async Task Should_include_listing_media_and_category_fields_in_the_review_row()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-media", listingSetup: entity =>
        {
            entity.PrimaryImageUrl = "https://example.test/primary.jpg";
            entity.ImageUrls = """["https://example.test/1.jpg","https://example.test/2.jpg"]""";
            entity.Description = "A great controller.";
            entity.Condition = "Used";
            entity.Category0Name = "Video Games";
            entity.Category1Name = "Controllers";
        });
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.5);

        var row = (await store.GetReviewQueue(familyId, versionId, questions: null, threshold: 0.9, take: 50, CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.PrimaryImageUrl, Is.EqualTo("https://example.test/primary.jpg"));
            Assert.That(row.ImageUrlsJson, Does.Contain("1.jpg"));
            Assert.That(row.Description, Is.EqualTo("A great controller."));
            Assert.That(row.Condition, Is.EqualTo("Used"));
            Assert.That(row.Category0Name, Is.EqualTo("Video Games"));
            Assert.That(row.Category1Name, Is.EqualTo("Controllers"));
        });
    }

    [Test]
    public async Task Should_count_rows_needing_review_per_question()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var first = await SeedListing(jobId, "m-first");
        var second = await SeedListing(jobId, "m-second");
        await SeedRow(first, versionId, "item_type", ClassificationSource.Model, confidence: 0.5);
        await SeedRow(second, versionId, "item_type", ClassificationSource.Model, confidence: 0.6);

        var summary = await store.GetReviewSummary(familyId, versionId, threshold: 0.9, CancellationToken.None);

        Assert.That(summary.Single(c => c.Question == "item_type").Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_exclude_rows_on_an_older_taxonomy_version_from_the_queue_and_summary()
    {
        var store = CreateStore();
        var (familyId, jobId, oldVersionId) = await SeedFamily();
        var newVersionId = await AddTaxonomyVersion(familyId);
        var listing = await SeedListing(jobId, "m-stale-version");
        await SeedRow(listing, oldVersionId, "item_type", ClassificationSource.Model, confidence: 0.5);

        var queue = await store.GetReviewQueue(familyId, newVersionId, questions: null, threshold: 0.9, take: 50, CancellationToken.None);
        var summary = await store.GetReviewSummary(familyId, newVersionId, threshold: 0.9, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(queue, Is.Empty);
            Assert.That(summary, Is.Empty);
        });
    }

    [Test]
    public async Task Should_return_the_taxonomy_version_id_for_a_classified_listing()
    {
        var store = CreateStore();
        var (_, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-classified");
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.95);

        var result = await store.GetTaxonomyVersionId(listing, CancellationToken.None);

        Assert.That(result, Is.EqualTo(versionId));
    }

    [Test]
    public async Task Should_return_null_taxonomy_version_id_for_an_unclassified_listing()
    {
        var store = CreateStore();
        var (_, jobId, _) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-unclassified");

        var result = await store.GetTaxonomyVersionId(listing, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Should_write_a_human_answer_and_re_gate_dependent_questions()
    {
        var store = CreateStore();
        var (_, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-gate");
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.6, choice: "dualsense_standard");
        await SeedRow(listing, versionId, "edition", ClassificationSource.Model, confidence: 0.9, choice: "standard_colour");
        await SeedRow(listing, versionId, "colour", ClassificationSource.Model, confidence: 0.9, choice: "white");
        var taxonomy = TaxonomyDocumentParser.Parse(GatedTaxonomyJson);

        var updated = await store.SetHumanAnswer(listing, "item_type", "console", taxonomy, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated!.Answers.Single(a => a.Question == "item_type").Source, Is.EqualTo(ClassificationSource.Human));
            Assert.That(updated.Answers.Single(a => a.Question == "item_type").Choice, Is.EqualTo("console"));
            Assert.That(updated.Answers.Single(a => a.Question == "edition").IsApplicable, Is.False);
            Assert.That(updated.Answers.Single(a => a.Question == "colour").IsApplicable, Is.False);
        });
    }

    [Test]
    public async Task Should_return_null_when_setting_a_human_answer_for_an_unknown_question()
    {
        var store = CreateStore();
        var (_, jobId, versionId) = await SeedFamily();
        var listing = await SeedListing(jobId, "m-unknown-question");
        await SeedRow(listing, versionId, "item_type", ClassificationSource.Model, confidence: 0.6);
        var taxonomy = TaxonomyDocumentParser.Parse(GatedTaxonomyJson);

        var updated = await store.SetHumanAnswer(listing, "not_a_question", "console", taxonomy, CancellationToken.None);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public async Task Should_export_only_listings_with_at_least_one_human_answer()
    {
        var store = CreateStore();
        var (familyId, jobId, versionId) = await SeedFamily();
        var humanListing = await SeedListing(jobId, "m-export-human");
        var modelOnlyListing = await SeedListing(jobId, "m-export-model-only");
        await SeedRow(humanListing, versionId, "item_type", ClassificationSource.Human, confidence: 1.0, choice: "console");
        await SeedRow(modelOnlyListing, versionId, "item_type", ClassificationSource.Model, confidence: 0.95, choice: "console");

        var exportRows = await store.GetLabelExportRows(familyId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exportRows, Has.Count.EqualTo(1));
            Assert.That(exportRows[0].ListingId, Is.EqualTo("m-export-human"));
            Assert.That(exportRows[0].TaxonomyVersion, Is.EqualTo(1));
        });
    }

    private ClassificationReviewStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());

    private async Task<SeededFamily> SeedFamily()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        var family = new ProductFamilyEntity
        {
            Key = $"family-{Guid.NewGuid():N}",
            Name = "Family",
            ModelName = "ps5-controller",
            CreatedUtc = DateTime.UtcNow
        };
        db.ProductFamilies.Add(family);
        await db.SaveChangesAsync();
        job.ProductFamilyId = family.Id;
        var version = new TaxonomyVersionEntity
        {
            ProductFamilyId = family.Id,
            Version = 1,
            QuestionsJson = GatedTaxonomyJson,
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(version);
        await db.SaveChangesAsync();
        return new SeededFamily(family.Id, job.Id, version.Id);
    }

    private async Task<int> AddTaxonomyVersion(int familyId)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var version = new TaxonomyVersionEntity
        {
            ProductFamilyId = familyId,
            Version = 2,
            QuestionsJson = GatedTaxonomyJson,
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(version);
        await db.SaveChangesAsync();
        return version.Id;
    }

    private async Task<int> SeedListing(int jobId, string listingId, Action<ListingEntity>? listingSetup = null)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Title = "Sony DualSense",
            Url = $"https://example.test/{listingId}",
            Price = 42,
            IsSold = false,
            CreatedUtc = DateTime.UtcNow
        };
        listingSetup?.Invoke(listing);
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private async Task SeedRow(
        int listingEntityId,
        int taxonomyVersionId,
        string question,
        ClassificationSource source,
        double confidence,
        bool isApplicable = true,
        string choice = "console")
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listingEntityId,
            TaxonomyVersionId = taxonomyVersionId,
            Question = question,
            Choice = choice,
            ResolvedChoice = isApplicable ? choice : null,
            IsApplicable = isApplicable,
            Confidence = confidence,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = source,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed record SeededFamily(int FamilyId, int JobId, int VersionId);
}
