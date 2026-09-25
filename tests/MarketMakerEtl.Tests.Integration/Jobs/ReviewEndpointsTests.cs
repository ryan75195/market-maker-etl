using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class ReviewEndpointsTests : JobsApiTestBase
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

    [Test]
    public async Task Should_order_the_review_queue_by_confidence_ascending_and_respect_take()
    {
        var seed = await SeedFamily();
        var lowest = await SeedListing(seed.JobId, "m-low");
        var middle = await SeedListing(seed.JobId, "m-mid");
        var highest = await SeedListing(seed.JobId, "m-high");
        await SeedRow(lowest, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");
        await SeedRow(middle, seed.VersionId, "item_type", ClassificationSource.Model, 0.7, "console");
        await SeedRow(highest, seed.VersionId, "item_type", ClassificationSource.Model, 0.8, "console");

        var response = await Client.GetAsync($"/api/families/{seed.FamilyId}/review?take=2");
        var items = await response.Content.ReadFromJsonAsync<List<ClassificationReviewItem>>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items![0].ListingId, Is.EqualTo(lowest));
            Assert.That(items[1].ListingId, Is.EqualTo(middle));
        });
    }

    [Test]
    public async Task Should_remove_a_listing_from_the_queue_after_a_human_answer_is_written()
    {
        var seed = await SeedFamily();
        var listing = await SeedListing(seed.JobId, "m-put");
        await SeedRow(listing, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");

        var putResponse = await Client.PutAsJsonAsync(
            $"/api/listings/{listing}/classification/item_type", new SetChoiceRequest("console"));
        var updated = await putResponse.Content.ReadFromJsonAsync<ListingClassificationView>();
        var queueResponse = await Client.GetAsync($"/api/families/{seed.FamilyId}/review");
        var queue = await queueResponse.Content.ReadFromJsonAsync<List<ClassificationReviewItem>>();

        Assert.Multiple(() =>
        {
            Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated!.Answers.Single(a => a.Question == "item_type").Source, Is.EqualTo(ClassificationSource.Human));
            Assert.That(queue, Is.Empty);
        });
    }

    [Test]
    public async Task Should_mark_edition_and_colour_not_applicable_when_item_type_changes_to_console()
    {
        var seed = await SeedFamily();
        var listing = await SeedListing(seed.JobId, "m-gate");
        await SeedRow(listing, seed.VersionId, "item_type", ClassificationSource.Model, 0.6, "dualsense_standard");
        await SeedRow(listing, seed.VersionId, "edition", ClassificationSource.Model, 0.9, "standard_colour");
        await SeedRow(listing, seed.VersionId, "colour", ClassificationSource.Model, 0.9, "white");

        var putResponse = await Client.PutAsJsonAsync(
            $"/api/listings/{listing}/classification/item_type", new SetChoiceRequest("console"));
        var updated = await putResponse.Content.ReadFromJsonAsync<ListingClassificationView>();

        Assert.Multiple(() =>
        {
            Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated!.Answers.Single(a => a.Question == "edition").IsApplicable, Is.False);
            Assert.That(updated.Answers.Single(a => a.Question == "colour").IsApplicable, Is.False);
        });
    }

    [Test]
    public async Task Should_confirm_the_models_answer_as_correct()
    {
        var seed = await SeedFamily();
        var listing = await SeedListing(seed.JobId, "m-confirm");
        await SeedRow(listing, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");

        var response = await Client.PostAsync($"/api/listings/{listing}/classification/item_type/confirm", null);
        var updated = await response.Content.ReadFromJsonAsync<ListingClassificationView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated!.Answers.Single(a => a.Question == "item_type").Source, Is.EqualTo(ClassificationSource.Human));
            Assert.That(updated.Answers.Single(a => a.Question == "item_type").Choice, Is.EqualTo("console"));
        });
    }

    [Test]
    public async Task Should_return_bad_request_for_an_unknown_option()
    {
        var seed = await SeedFamily();
        var listing = await SeedListing(seed.JobId, "m-bad-option");
        await SeedRow(listing, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");

        var response = await Client.PutAsJsonAsync(
            $"/api/listings/{listing}/classification/item_type", new SetChoiceRequest("not_a_real_choice"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Should_return_bad_request_for_an_unknown_question()
    {
        var seed = await SeedFamily();
        var listing = await SeedListing(seed.JobId, "m-bad-question");
        await SeedRow(listing, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");

        var response = await Client.PutAsJsonAsync(
            $"/api/listings/{listing}/classification/not_a_real_question", new SetChoiceRequest("console"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Should_return_not_found_for_an_unknown_listing()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/listings/999999/classification/item_type", new SetChoiceRequest("console"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Should_return_review_summary_counts()
    {
        var seed = await SeedFamily();
        var first = await SeedListing(seed.JobId, "m-summary-first");
        var second = await SeedListing(seed.JobId, "m-summary-second");
        await SeedRow(first, seed.VersionId, "item_type", ClassificationSource.Model, 0.5, "console");
        await SeedRow(second, seed.VersionId, "item_type", ClassificationSource.Model, 0.6, "console");

        var response = await Client.GetAsync($"/api/families/{seed.FamilyId}/review/summary");
        var summary = await response.Content.ReadFromJsonAsync<ClassificationReviewSummary>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(summary!.Questions.Single(q => q.Question == "item_type").Count, Is.EqualTo(2));
            Assert.That(summary.Total, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_export_labels_jsonl_with_human_answers_and_threshold_rule()
    {
        var seed = await SeedFamily();
        var humanListing = await SeedListing(seed.JobId, "m-export-human");
        var modelOnlyListing = await SeedListing(seed.JobId, "m-export-model-only");
        await SeedRow(humanListing, seed.VersionId, "item_type", ClassificationSource.Human, 1.0, "console");
        await SeedRow(modelOnlyListing, seed.VersionId, "item_type", ClassificationSource.Model, 0.95, "console");

        var response = await Client.GetAsync($"/api/families/{seed.FamilyId}/labels.jsonl");
        var text = await response.Content.ReadAsStringAsync();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var exported = JsonSerializer.Deserialize<ClassificationLabelExport>(lines.Single());

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(text, Does.EndWith("\n"));
            Assert.That(exported!.ListingId, Is.EqualTo("m-export-human"));
            Assert.That(exported.Answers["item_type"], Is.EqualTo("console"));
        });
    }

    private async Task<SeededFamily> SeedFamily()
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

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

    private async Task<int> SeedListing(int jobId, string listingId)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var listing = new ListingEntity
        {
            ListingId = listingId,
            ScrapeJobId = jobId,
            Marketplace = Marketplace.Mercari,
            Title = "Sony DualSense",
            Url = $"https://example.test/{listingId}",
            Price = 42,
            IsSold = false,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();
        return listing.Id;
    }

    private async Task SeedRow(
        int listingEntityId, int taxonomyVersionId, string question, ClassificationSource source, double confidence, string choice)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();
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
            Source = source,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed record SeededFamily(int FamilyId, int JobId, int VersionId);

    private sealed record SetChoiceRequest(string Choice);
}
