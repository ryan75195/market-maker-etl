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
public class PriceGroupHistoryEndpointsTests : JobsApiTestBase
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
    public async Task Should_bucket_sold_listings_by_week_across_three_weeks()
    {
        var seeded = await SeedFamilyWithTaxonomy();
        var currentWeekMonday = MondayOf(DateTime.UtcNow);
        await SeedListingSoldAt(seeded, "white-current-week", soldPrice: 100m, soldDate: currentWeekMonday.AddDays(2));
        await SeedListingSoldAt(seeded, "white-previous-week", soldPrice: 120m, soldDate: currentWeekMonday.AddDays(-5));
        await SeedListingSoldAt(seeded, "white-two-weeks-ago", soldPrice: 140m, soldDate: currentWeekMonday.AddDays(-12));

        var response = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/history?where=colour:white&weeks=3");
        var buckets = await response.Content.ReadFromJsonAsync<List<PriceGroupHistoryBucket>>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(buckets, Has.Count.EqualTo(3));
            Assert.That(buckets!.Select(b => b.BucketStart), Is.EqualTo(new[]
            {
                currentWeekMonday.AddDays(-14),
                currentWeekMonday.AddDays(-7),
                currentWeekMonday
            }));
            Assert.That(buckets!.Select(b => b.SoldCount), Is.EqualTo(new[] { 1, 1, 1 }));
        });
    }

    [Test]
    public async Task Should_exclude_estimated_sold_dates_by_default()
    {
        var seeded = await SeedFamilyWithTaxonomy();
        await SeedClassifiedListing(seeded, "white-estimated", soldPrice: 100m, soldDaysAgo: null);

        var excluded = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/history?where=colour:white&weeks=1");
        var included = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/history?where=colour:white&weeks=1&includeEstimatedDates=true");

        var excludedBuckets = await excluded.Content.ReadFromJsonAsync<List<PriceGroupHistoryBucket>>();
        var includedBuckets = await included.Content.ReadFromJsonAsync<List<PriceGroupHistoryBucket>>();

        Assert.Multiple(() =>
        {
            Assert.That(excludedBuckets!.Sum(b => b.SoldCount), Is.EqualTo(0));
            Assert.That(includedBuckets!.Sum(b => b.SoldCount), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_return_bad_request_for_an_invalid_bucket_or_basis()
    {
        var seeded = await SeedFamilyWithTaxonomy();

        var invalidBucket = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/history?bucket=month");
        var invalidBasis = await Client.GetAsync(
            $"/api/families/{seeded.FamilyId}/price-groups/history?basis=gross");

        Assert.Multiple(() =>
        {
            Assert.That(invalidBucket.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalidBasis.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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
            Key = $"price-group-history-{Guid.NewGuid():N}",
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

    private Task SeedClassifiedListing(SeededFamily seeded, string title, decimal soldPrice, int? soldDaysAgo) =>
        SeedListingSoldAt(
            seeded, title, soldPrice, soldDaysAgo.HasValue ? DateTime.UtcNow.AddDays(-soldDaysAgo.Value) : null);

    private async Task SeedListingSoldAt(
        SeededFamily seeded, string title, decimal soldPrice, DateTime? soldDate)
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
            IsSold = true,
            SoldPrice = soldPrice,
            SoldDate = soldDate,
            CreatedUtc = DateTime.UtcNow
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        db.ListingClassifications.Add(new ListingClassificationEntity
        {
            ListingEntityId = listing.Id,
            TaxonomyVersionId = seeded.TaxonomyVersionId,
            Question = "colour",
            Choice = "white",
            ResolvedChoice = "white",
            IsApplicable = true,
            Confidence = 0.95,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = ClassificationSource.Model,
            ClassifiedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static DateTime MondayOf(DateTime instant)
    {
        var date = instant.Date;
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private sealed record SeededFamily(int FamilyId, int TaxonomyVersionId, int JobId);
}
