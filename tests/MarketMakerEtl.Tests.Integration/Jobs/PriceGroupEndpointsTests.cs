using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class PriceGroupEndpointsTests : JobsApiTestBase
{
    private const string TaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "colour": {
           "instructions": "Which colour?",
           "criteria": { "white": "White.", "midnight_black": "Black." }
          }
         }
        }
        """;

    [Test]
    public async Task Should_group_listings_by_colour_and_report_sold_counts_and_median()
    {
        var seeded = await SeedFamilyWithTaxonomy();
        await SeedClassifiedListing(seeded, "white-1", colour: "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1);
        await SeedClassifiedListing(seeded, "white-2", colour: "white", isSold: true, soldPrice: 120m, soldDaysAgo: 2);
        await SeedClassifiedListing(seeded, "black-1", colour: "midnight_black", isSold: true, soldPrice: 200m, soldDaysAgo: 1);

        var response = await Client.GetAsync($"/api/families/{seeded.FamilyId}/price-groups?by=colour");
        var groups = await response.Content.ReadFromJsonAsync<List<PriceGroupSummary>>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups![0].Key["colour"], Is.EqualTo("white"));
            Assert.That(groups[0].SoldCount, Is.EqualTo(2));
            Assert.That(groups[0].SoldMedian, Is.EqualTo(110m));
            Assert.That(groups[1].Key["colour"], Is.EqualTo("midnight_black"));
            Assert.That(groups[1].SoldCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_return_bad_request_for_an_unknown_question_or_option()
    {
        var seeded = await SeedFamilyWithTaxonomy();

        var unknownQuestion = await Client.GetAsync($"/api/families/{seeded.FamilyId}/price-groups?where=nope:white");
        var unknownOption = await Client.GetAsync($"/api/families/{seeded.FamilyId}/price-groups?where=colour:neon");

        Assert.Multiple(() =>
        {
            Assert.That(unknownQuestion.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(unknownOption.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task Should_hide_groups_with_fewer_sold_listings_than_min_sold()
    {
        var seeded = await SeedFamilyWithTaxonomy();
        await SeedClassifiedListing(seeded, "white-1", colour: "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1);

        var response = await Client.GetAsync($"/api/families/{seeded.FamilyId}/price-groups?by=colour&minSold=2");
        var groups = await response.Content.ReadFromJsonAsync<List<PriceGroupSummary>>();

        Assert.That(groups, Is.Empty);
    }

    [Test]
    public async Task Should_order_active_listings_in_the_group_listings_endpoint_by_delta_from_sold_median()
    {
        var seeded = await SeedFamilyWithTaxonomy();
        await SeedClassifiedListing(seeded, "white-sold", colour: "white", isSold: true, soldPrice: 100m, soldDaysAgo: 1);
        await SeedClassifiedListing(seeded, "white-active-far", colour: "white", isSold: false, price: 150m);
        await SeedClassifiedListing(seeded, "white-active-near", colour: "white", isSold: false, price: 90m);

        var response = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/listings?where=colour:white&status=active");
        var listings = await response.Content.ReadFromJsonAsync<List<PriceGroupListingResult>>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(listings, Has.Count.EqualTo(2));
            Assert.That(listings![0].Title, Is.EqualTo("white-active-near"));
            Assert.That(listings[0].DeltaFromSoldMedian, Is.EqualTo(-10m));
            Assert.That(listings[1].Title, Is.EqualTo("white-active-far"));
        });
    }

    private async Task<SeededFamily> SeedFamilyWithTaxonomy()
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();

        var family = new ProductFamilyEntity
        {
            Key = $"price-groups-{Guid.NewGuid():N}",
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
            QuestionsJson = TaxonomyJson,
            CreatedUtc = DateTime.UtcNow
        };
        db.TaxonomyVersions.Add(taxonomyVersion);
        await db.SaveChangesAsync();

        return new SeededFamily(family.Id, taxonomyVersion.Id, job.Id);
    }

    private async Task SeedClassifiedListing(
        SeededFamily seeded,
        string title,
        string colour,
        bool isSold,
        decimal? soldPrice = null,
        int? soldDaysAgo = null,
        decimal? price = null)
    {
        var dbContextFactory = Factory.Services.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var listing = new ListingEntity
        {
            ListingId = $"m{Guid.NewGuid():N}"[..12],
            ScrapeJobId = seeded.JobId,
            Marketplace = Marketplace.Mercari,
            Title = title,
            Currency = "USD",
            IsSold = isSold,
            SoldPrice = soldPrice,
            Price = price,
            SoldDate = soldDaysAgo.HasValue ? DateTime.UtcNow.AddDays(-soldDaysAgo.Value) : null,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listing.Id,
            TaxonomyVersionId = seeded.TaxonomyVersionId,
            Question = "colour",
            Choice = colour,
            ResolvedChoice = colour,
            IsApplicable = true,
            Confidence = 0.95,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed record SeededFamily(int FamilyId, int TaxonomyVersionId, int JobId);
}
