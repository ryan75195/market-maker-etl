using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class ClassificationEndpointsTests : JobsApiTestBase
{
    [Test]
    public async Task Should_return_not_found_for_a_listing_with_no_classification()
    {
        var response = await Client.GetAsync("/api/listings/123456/classification");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Should_return_the_classification_rows_for_a_classified_listing()
    {
        var listingEntityId = await SeedClassifiedListing();

        var response = await Client.GetAsync($"/api/listings/{listingEntityId}/classification");
        var classification = await response.Content.ReadFromJsonAsync<ListingClassificationView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(classification!.ListingEntityId, Is.EqualTo(listingEntityId));
            Assert.That(classification.TaxonomyVersion, Is.EqualTo(1));
            Assert.That(classification.Answers.Single().Question, Is.EqualTo("item_type"));
            Assert.That(classification.Answers.Single().Choice, Is.EqualTo("console"));
        });
    }

    private async Task<int> SeedClassifiedListing()
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var family = new ProductFamilyEntity
        {
            Key = "ps5-controller-endpoints",
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

        var listing = new ListingEntity
        {
            ListingId = "m00000000010",
            ScrapeJobId = job.Id,
            Marketplace = Marketplace.Mercari,
            Title = "Sony PlayStation 5 console",
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listing.Id,
            TaxonomyVersionId = taxonomyVersion.Id,
            Question = "item_type",
            Choice = "console",
            ResolvedChoice = "console",
            IsApplicable = true,
            Confidence = 0.95,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return listing.Id;
    }
}
