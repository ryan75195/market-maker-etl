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

    private static OpenAiOptions OpenAiOpts(string apiKey = "test-key") =>
        new(apiKey, "gpt-6-luna", "low", 25, 6, 120);

    private static IClassificationThrottleService Throttle(int budget = 2000)
    {
        var throttle = Substitute.For<IClassificationThrottleService>();
        throttle.IsBackingOffFailures().Returns(false);
        throttle.IsProbingAfterFailures().Returns(false);
        throttle.ResolveTickBudget().Returns(budget);
        return throttle;
    }

    private static void NoOpFailureCallback(ClassificationBatchFailure failure)
    {
    }

    [Test]
    public async Task Should_do_nothing_when_the_api_key_is_not_configured()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        var service = new ListingClassificationService(
            families, classifications, client, Throttle(), OpenAiOpts(apiKey: ""));

        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(0));
            Assert.That(result.ListingsSelected, Is.EqualTo(0));
        });
        await families.DidNotReceive().GetJobsWithFamily(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_classify_pending_listings_for_a_job_with_a_family()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(42, "Sony DualSense", "Games", "Accessories", "Controllers", "Sony", "Barely used.", false);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                1,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95, ["other"] = 0.05 })
                })]));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
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
            Assert.That(sentRequest.States[0].Category, Is.EqualTo("Games > Accessories > Controllers"));
        });
        await classifications.Received(1).UpsertBatch(
            Arg.Is<IReadOnlyList<ListingClassificationBatchItem>>(batch =>
                batch.Count == 1 &&
                batch[0].ListingEntityId == 42 &&
                batch[0].Rows.Single().Choice == "console"),
            Arg.Any<CancellationToken>());
        await classifications.Received(1).RecordBatchOutcome(true, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_treat_a_human_choice_as_authoritative_for_gating_dependent_questions()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildPs5Family());
        var target = new ListingClassificationTarget(42, "Sony DualSense", null, null, null, null, null, false);
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
                1,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("dualsense_standard", 0.9, 1.0, new Dictionary<string, double> { ["dualsense_standard"] = 0.9 }),
                    ["edition"] = new("standard_colour", 0.9, 1.0, new Dictionary<string, double> { ["standard_colour"] = 0.9 }),
                    ["colour"] = new("white", 0.9, 1.0, new Dictionary<string, double> { ["white"] = 0.9 })
                })]));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
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
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(7, "Title", null, null, null, null, null, false);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns<ClassifyResponse>(_ => throw new ListingClassifierException("classifier is down"));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ListingsClassified, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].ErrorMessage, Does.Contain("classifier is down"));
        });
        await classifications.DidNotReceive().UpsertBatch(
            Arg.Any<IReadOnlyList<ListingClassificationBatchItem>>(), Arg.Any<CancellationToken>());
        await classifications.Received(1).RecordBatchOutcome(false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_continue_to_the_next_job_after_a_batch_timeout()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>())
            .Returns([BuildJob(productFamilyId: 1, id: 10), BuildJob(productFamilyId: 2, id: 20)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        families.GetFamily(2, Arg.Any<CancellationToken>()).Returns(BuildFamily(familyId: 2));
        var targets = new[]
        {
            new ListingClassificationTarget(1, "First", null, null, null, null, null, false),
            new ListingClassificationTarget(2, "Second", null, null, null, null, null, false)
        };
        classifications.GetListingsNeedingClassification(10, 100, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(targets);
        classifications.GetListingsNeedingClassification(20, 100, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns<ClassifyResponse>(_ => throw ListingClassifierException.Timeout(
                "OpenAI request timed out after 120s."));

        var reportedFailures = new List<ClassificationBatchFailure>();
        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(reportedFailures.Add, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(2));
            Assert.That(result.ListingsSelected, Is.EqualTo(2));
            Assert.That(result.ListingsClassified, Is.EqualTo(0));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].JobId, Is.EqualTo(10));
            Assert.That(reportedFailures, Has.Count.EqualTo(1));
            Assert.That(reportedFailures[0].JobId, Is.EqualTo(10));
        });
        await client.Received(1).Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>());
        await families.Received(1).GetFamily(2, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_do_nothing_when_the_throttle_is_backing_off_failures()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        var throttle = Throttle();
        throttle.IsBackingOffFailures().Returns(true);

        var service = new ListingClassificationService(families, classifications, client, throttle, OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await families.DidNotReceive().GetJobsWithFamily(Arg.Any<CancellationToken>());
        throttle.DidNotReceive().ObserveTickResult(Arg.Any<ClassificationTickResult>());
    }

    [Test]
    public async Task Should_report_the_tick_result_to_the_throttle_after_classifying()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(42, "Sony DualSense", null, null, null, null, null, false);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                1,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95 })
                })]));
        var throttle = Throttle();

        var service = new ListingClassificationService(families, classifications, client, throttle, OpenAiOpts());
        await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        throttle.Received(1).ObserveTickResult(Arg.Is<ClassificationTickResult>(
            result => result.ListingsSelected == 1 && result.ListingsClassified == 1));
    }

    [Test]
    public async Task Should_stop_after_the_first_fully_failed_job_when_probing_after_earlier_failures()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>())
            .Returns([BuildJob(productFamilyId: 1, id: 10), BuildJob(productFamilyId: 2, id: 20)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        families.GetFamily(2, Arg.Any<CancellationToken>()).Returns(BuildFamily(familyId: 2));
        var targets = new[] { new ListingClassificationTarget(1, "First", null, null, null, null, null, false) };
        classifications.GetListingsNeedingClassification(10, 100, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(targets);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns<ClassifyResponse>(_ => throw new ListingClassifierException("OpenAI returned 401 Unauthorized"));
        var throttle = Throttle(budget: 25);
        throttle.IsProbingAfterFailures().Returns(true);

        var service = new ListingClassificationService(families, classifications, client, throttle, OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(1));
            Assert.That(result.ListingsClassified, Is.EqualTo(0));
        });
        await families.DidNotReceive().GetFamily(2, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_upsert_successful_listings_and_report_a_failure_for_a_partial_batch()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1, id: 10)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var targets = new[]
        {
            new ListingClassificationTarget(1, "First", null, null, null, null, null, false),
            new ListingClassificationTarget(2, "Second", null, null, null, null, null, false)
        };
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns(targets);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                1,
                [
                    new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                    {
                        ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95 })
                    }),
                    new ClassifyResult(new Dictionary<string, ClassifyAnswer>(), "No response received for this listing.")
                ]));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ListingsSelected, Is.EqualTo(2));
            Assert.That(result.ListingsClassified, Is.EqualTo(1));
            Assert.That(result.Failures, Has.Count.EqualTo(1));
        });
        await classifications.Received(1).UpsertBatch(
            Arg.Is<IReadOnlyList<ListingClassificationBatchItem>>(batch => batch.Count == 1 && batch[0].ListingEntityId == 1),
            Arg.Any<CancellationToken>());
        await classifications.Received(1).RecordBatchOutcome(false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_skip_jobs_without_a_product_family()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: null)]);

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await families.DidNotReceive().GetFamily(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_classify_a_disabled_job_that_has_a_product_family()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();

        families.GetJobsWithFamily(Arg.Any<CancellationToken>())
            .Returns([BuildJob(productFamilyId: 1, isEnabled: false)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var target = new ListingClassificationTarget(42, "Sony DualSense", null, null, null, null, null, false);
        classifications.GetListingsNeedingClassification(10, 100, 2000, Arg.Any<CancellationToken>())
            .Returns([target]);
        client.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                1,
                [new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                {
                    ["item_type"] = new("console", 0.95, 1.0, new Dictionary<string, double> { ["console"] = 0.95 })
                })]));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.JobsProcessed, Is.EqualTo(1));
            Assert.That(result.ListingsClassified, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Should_skip_a_job_whose_family_has_no_taxonomy_version_yet()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>())
            .Returns(new ProductFamilyView(1, "ps5-controller", "PS5 Controller", "ps5-controller", DateTime.UtcNow, null));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await classifications.DidNotReceive().GetListingsNeedingClassification(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_skip_a_job_whose_family_is_still_in_draft_state()
    {
        var families = Substitute.For<IProductFamilyStore>();
        var classifications = Substitute.For<IListingClassificationStore>();
        var client = Substitute.For<IListingClassifierClient>();
        families.GetJobsWithFamily(Arg.Any<CancellationToken>()).Returns([BuildJob(productFamilyId: 1)]);
        families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily(state: FamilyState.Draft));

        var service = new ListingClassificationService(families, classifications, client, Throttle(), OpenAiOpts());
        var result = await service.ClassifyPending(NoOpFailureCallback, CancellationToken.None);

        Assert.That(result.JobsProcessed, Is.EqualTo(0));
        await classifications.DidNotReceive().GetListingsNeedingClassification(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static JobView BuildJob(int? productFamilyId, int id = 10, bool isEnabled = true) =>
        new(id, "ps5 controller", Marketplace.Mercari, null, 24, isEnabled, null, null, DateTime.UtcNow, [], productFamilyId);

    private static ProductFamilyView BuildFamily(int familyId = 1, FamilyState state = FamilyState.Active) =>
        new(
            familyId,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, familyId, 1, TaxonomyJson, DateTime.UtcNow),
            State: state);

    private static ProductFamilyView BuildPs5Family() =>
        new(
            1,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, 1, 1, GatedTaxonomyJson, DateTime.UtcNow));
}
