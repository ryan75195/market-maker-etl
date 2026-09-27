using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilyOnboardingPreviewServiceTests
{
    private const string TaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": { "controller": "A controller.", "other": "Something else." }
          }
         }
        }
        """;

    private IFamilyOnboardingStore _onboarding = null!;
    private IListingClassifierClient _classifier = null!;
    private IProductFamilyStore _families = null!;
    private FamilyOnboardingPreviewService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _onboarding = Substitute.For<IFamilyOnboardingStore>();
        _classifier = Substitute.For<IListingClassifierClient>();
        _families = Substitute.For<IProductFamilyStore>();
        _service = new FamilyOnboardingPreviewService(_onboarding, _classifier, _families);
    }

    [Test]
    public async Task Should_do_nothing_when_the_family_has_no_taxonomy_version()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>())
            .Returns(new ProductFamilyView(1, "k", "N", "k", DateTime.UtcNow, null));
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns(BuildSnapshot());

        await _service.RunPreview(1, CancellationToken.None);

        await _classifier.DidNotReceive().Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_do_nothing_when_there_is_no_sample_to_classify()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns(BuildSnapshot(sample: []));

        await _service.RunPreview(1, CancellationToken.None);

        await _classifier.DidNotReceive().Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_classify_the_sample_and_save_a_per_question_distribution()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        var sample = new[]
        {
            new FamilySampleListing("m1", "Title 1", "Desc 1", null, null, false, 10m, null),
            new FamilySampleListing("m2", "Title 2", "Desc 2", null, null, true, 15m, null)
        };
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns(BuildSnapshot(sample));
        _classifier.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ClassifyResponse(
                "ps5-controller",
                1,
                [
                    new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                    {
                        ["item_type"] = new("controller", 0.9, 1.0, new Dictionary<string, double>())
                    }),
                    new ClassifyResult(new Dictionary<string, ClassifyAnswer>
                    {
                        ["item_type"] = new("controller", 0.8, 1.0, new Dictionary<string, double>())
                    })
                ]));

        await _service.RunPreview(1, CancellationToken.None);

        var sentRequest = (ClassifyRequest)_classifier.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(sentRequest.Model, Is.EqualTo("ps5-controller"));
            Assert.That(sentRequest.States, Has.Count.EqualTo(2));
        });
        await _onboarding.Received(1).SavePreview(
            1,
            Arg.Is<IReadOnlyList<OnboardingQuestionDistributionView>>(preview =>
                preview.Count == 1 &&
                preview[0].Question == "item_type" &&
                preview[0].Choices.Single().Choice == "controller" &&
                preview[0].Choices.Single().Count == 2 &&
                preview[0].Choices.Single().Examples.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_return_null_when_getting_onboarding_for_a_family_without_a_snapshot()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns((FamilyOnboardingSnapshot?)null);

        var result = await _service.GetOnboarding(1, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Should_compose_an_onboarding_view_from_the_family_and_snapshot()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily());
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns(BuildSnapshot());

        var result = await _service.GetOnboarding(1, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.FamilyId, Is.EqualTo(1));
            Assert.That(result.State, Is.EqualTo(FamilyState.Draft));
            Assert.That(result.SearchTerm, Is.EqualTo("ps5 controller"));
            Assert.That(result.TaxonomyVersion, Is.EqualTo(1));
            Assert.That(result.SampleSize, Is.EqualTo(1));
        });
    }

    private static ProductFamilyView BuildFamily() =>
        new(
            1,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, 1, 1, TaxonomyJson, DateTime.UtcNow),
            State: FamilyState.Draft);

    private static FamilyOnboardingSnapshot BuildSnapshot(IReadOnlyList<FamilySampleListing>? sample = null) =>
        new(
            1,
            10,
            "ps5 controller",
            sample ?? [new FamilySampleListing("m1", "Title", "Desc", null, null, false, 10m, null)],
            [],
            0,
            0,
            0m,
            null,
            DateTime.UtcNow);
}
