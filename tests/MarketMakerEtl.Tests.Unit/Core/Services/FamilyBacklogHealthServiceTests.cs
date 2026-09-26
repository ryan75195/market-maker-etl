using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilyBacklogHealthServiceTests
{
    [Test]
    public async Task Should_return_no_families_when_none_exist()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var reviews = Substitute.For<IClassificationReviewStore>();
        families.GetFamilies(Arg.Any<CancellationToken>()).Returns([]);
        var service = CreateService(families, classifications, reviews);

        var health = await service.GetFamilyBacklogHealth(CancellationToken.None);

        Assert.That(health, Is.Empty);
    }

    [Test]
    public async Task Should_report_zero_backlog_for_a_family_without_a_taxonomy_version()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var reviews = Substitute.For<IClassificationReviewStore>();
        families.GetFamilies(Arg.Any<CancellationToken>()).Returns([BuildFamily(1, taxonomyVersion: null)]);
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([]);
        var service = CreateService(families, classifications, reviews);

        var health = await service.GetFamilyBacklogHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Single().PendingClassificationCount, Is.EqualTo(0));
            Assert.That(health.Single().NeedsReviewCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Should_sum_pending_classification_across_the_families_jobs()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var reviews = Substitute.For<IClassificationReviewStore>();
        families.GetFamilies(Arg.Any<CancellationToken>()).Returns([BuildFamily(1, taxonomyVersion: 100)]);
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(10, 1), BuildJob(20, 1)]);
        classifications.CountListingsNeedingClassification(10, 100, Arg.Any<CancellationToken>()).Returns(3);
        classifications.CountListingsNeedingClassification(20, 100, Arg.Any<CancellationToken>()).Returns(4);
        reviews.GetReviewSummary(1, 100, Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns([new ClassificationReviewCount("item_type", 2)]);
        var service = CreateService(families, classifications, reviews);

        var health = await service.GetFamilyBacklogHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Single().PendingClassificationCount, Is.EqualTo(7));
            Assert.That(health.Single().NeedsReviewCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_not_count_jobs_belonging_to_a_different_family()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var reviews = Substitute.For<IClassificationReviewStore>();
        families.GetFamilies(Arg.Any<CancellationToken>()).Returns([BuildFamily(1, taxonomyVersion: 100)]);
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(10, 2)]);
        reviews.GetReviewSummary(1, 100, Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = CreateService(families, classifications, reviews);

        var health = await service.GetFamilyBacklogHealth(CancellationToken.None);

        Assert.That(health.Single().PendingClassificationCount, Is.EqualTo(0));
        await classifications.DidNotReceive().CountListingsNeedingClassification(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_count_a_disabled_job_that_has_a_family()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var reviews = Substitute.For<IClassificationReviewStore>();
        families.GetFamilies(Arg.Any<CancellationToken>()).Returns([BuildFamily(1, taxonomyVersion: 100)]);
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(10, 1, isEnabled: false)]);
        classifications.CountListingsNeedingClassification(10, 100, Arg.Any<CancellationToken>()).Returns(5);
        reviews.GetReviewSummary(1, 100, Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = CreateService(families, classifications, reviews);

        var health = await service.GetFamilyBacklogHealth(CancellationToken.None);

        Assert.That(health.Single().PendingClassificationCount, Is.EqualTo(5));
        await classifications.Received(1).CountListingsNeedingClassification(10, 100, Arg.Any<CancellationToken>());
    }

    private static FamilyBacklogHealthService CreateService(
        IProductFamilyStore families,
        IListingClassificationStore classifications,
        IClassificationReviewStore reviews) =>
        new(families, classifications, reviews, new ClassificationReviewOptions(0.9));

    private static ProductFamilyView BuildFamily(int id, int? taxonomyVersion) =>
        new(
            id,
            $"family-{id}",
            "Family",
            "model",
            DateTime.UtcNow,
            taxonomyVersion is null ? null : new TaxonomyVersionView(taxonomyVersion.Value, id, 1, "{}", DateTime.UtcNow));

    private static JobView BuildJob(int id, int productFamilyId, bool isEnabled = true) =>
        new(id, "search", Marketplace.Mercari, null, 24, isEnabled, null, null, DateTime.UtcNow, [], productFamilyId);
}
