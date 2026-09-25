using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingClassificationServiceTests
{
    private const string TaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": { "console": "A console.", "other": "Something else." }
          }
         }
        }
        """;

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

    private static ClassifierOptions Options(string baseUrl = "http://classifier.test", int batchSize = 64) =>
        new(baseUrl, batchSize, 5, 2000, 120);

    private static void NoOpFailureCallback(ClassificationBatchFailure failure)
    {
    }

    [Test]
    public async Task Should_do_nothing_when_the_base_url_is_not_configured()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        var service = new ListingClassificationService(jobs, families, classifications, client, Options(baseUrl: ""));

        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(0));
            Assert.That(result.ListingsSelected, Is.EqualTo(0));
        });
        await jobs.DidNotReceive().GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_classify_pending_listings_for_a_job_with_a_family()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(42, "Sony DualSense", "Games", "Accessories", "Controllers", "Sony", "Barely used.");
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                3,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95, ["other"] = 0.05 })
                })]));

        var service = new ListingClassificationService(jobs, families, classifications, client, Options());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        var sentRequest = (ClassifyRequest)client.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(1));
            Assert.That(result.ListingsSelected, Is.EqualTo(1));
            Assert.That(result.ListingsClassified, Is.EqualTo(1));
            Assert.That(result.Failures, Is.Empty);
            Assert.That(sentRequest.Model, Is.EqualTo("ps5-controller"));
            Assert.That(sentRequest.States[0].Title, Is.EqualTo("Sony DualSense"));
            Assert.That(sentRequest.States[0].MercariCategory, Is.EqualTo("Games > Accessories > Controllers"));
        });
        await classifications.Received(1).UpsertBatch(
            Arg.Is<IReadOnlyList<ListingClassificationBatchItem>>(batch =>
                batch.Count == 1 &&
                batch[0].ListingEntityId == 42 &&
                batch[0].Rows.Single().Choice == "console"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_treat_a_human_choice_as_authoritative_for_gating_dependent_questions()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildPs5Family());
        var target = new ListingClassificationTarget(42, "Sony DualSense", null, null, null, null, null);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        classifications.GetHumanChoices(
                Arg.Is<IReadOnlyList<int>>(ids => ids.Contains(42)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, IReadOnlyDictionary<string, string>>
            {
                [42] = new Dictionary<string, string> { ["item_type"] = "console" }
            });
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                3,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("dualsense_standard", 0.9, 1.0, new Dictionary<string, double> { ["dualsense_standard"] = 0.9 }),
                    ["edition"] = new("standard_colour", 0.9, 1.0, new Dictionary<string, double> { ["standard_colour"] = 0.9 }),
                    ["colour"] = new("white", 0.9, 1.0, new Dictionary<string, double> { ["white"] = 0.9 })
                })]));

        var service = new ListingClassificationService(jobs, families, classifications, client, Options());
        await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        await classifications.Received(1).UpsertBatch(
            Arg.Is<IReadOnlyList<ListingClassificationBatchItem>>(batch =>
                !batch[0].Rows.Single(r => r.Question == "edition").IsApplicable &&
                !batch[0].Rows.Single(r => r.Question == "colour").IsApplicable),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_record_a_batch_failure_and_continue_when_the_client_throws()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(7, "Title", null, null, null, null, null);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns<ClassifyResponse>(_ => throw new ListingClassifierException("classifier is down"));

        var service = new ListingClassificationService(jobs, families, classifications, client, Options());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ListingsClassified, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].ErrorMessage, Does.Contain("classifier is down"));
        });
        await classifications.DidNotReceive().UpsertBatch(
            Arg.Any<IReadOnlyList<ListingClassificationBatchItem>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_stop_further_batches_and_jobs_after_a_batch_times_out()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>())
            .Returns([BuildJob(productFamilyId: 1, id: 10), BuildJob(productFamilyId: 2, id: 20)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var targets = new[]
        {
            new ListingClassificationTarget(1, "First", null, null, null, null, null),
            new ListingClassificationTarget(2, "Second", null, null, null, null, null)
        };
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns(targets);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns<ClassifyResponse>(_ => throw ListingClassifierException.Timeout(
                "Classifying against http://classifier.test timed out after 120s."));

        var reportedFailures = new List<ClassificationBatchFailure>();
        var service = new ListingClassificationService(jobs, families, classifications, client, Options(batchSize: 1));
        var result = await service.ClassifyPending(reportedFailures.Add, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(1));
            Assert.That(result.ListingsSelected, Is.EqualTo(2));
            Assert.That(result.ListingsClassified, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].JobId, Is.EqualTo(10));
            Assert.That(reportedFailures, Has.Count.EqualTo(1));
            Assert.That(reportedFailures[0].JobId, Is.EqualTo(10));
        });
        await client.Received(1).Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>());
        await families.DidNotReceive().GetFamily(2, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_continue_to_the_next_batch_after_a_non_timeout_failure()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1, id: 10)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var targets = new[]
        {
            new ListingClassificationTarget(1, "First", null, null, null, null, null),
            new ListingClassificationTarget(2, "Second", null, null, null, null, null)
        };
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns(targets);

        var callCount = 0;
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new ListingClassifierException("Classifier returned 500 Internal Server Error.");
            }

            return Task.FromResult(new ClassifyResponse(
                "ps5-controller",
                3,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95 })
                })]));
        });

        var service = new ListingClassificationService(jobs, families, classifications, client, Options(batchSize: 1));
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ListingsSelected, Is.EqualTo(2));
            Assert.That(result.ListingsClassified, Is.EqualTo(1));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].ErrorMessage, Does.Contain("500"));
        });
        await client.Received(2).Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_skip_jobs_without_a_product_family()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: null)]);

        var service = new ListingClassificationService(jobs, families, classifications, client, Options());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await families.DidNotReceive().GetFamily(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_skip_a_job_whose_family_has_no_taxonomy_version_yet()
    {
        var jobs = Substitute.For<IJobStore>();
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>())
            .Returns(new ProductFamilyView(1, "ps5-controller", "PS5 Controller", "ps5-controller", DateTime.UtcNow, null));

        var service = new ListingClassificationService(jobs, families, classifications, client, Options());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await classifications.DidNotReceive().GetListingsNeedingClassification(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static JobView BuildJob(int? productFamilyId, int id = 10) =>
        new(id, "ps5 controller", Marketplace.Mercari, null, 24, true, null, null, DateTime.UtcNow, [], productFamilyId);

    private static ProductFamilyView BuildFamily() =>
        new(
            1,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, 1, 1, TaxonomyJson, DateTime.UtcNow));

    private static ProductFamilyView BuildPs5Family() =>
        new(
            1,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, 1, 1, GatedTaxonomyJson, DateTime.UtcNow));
}
