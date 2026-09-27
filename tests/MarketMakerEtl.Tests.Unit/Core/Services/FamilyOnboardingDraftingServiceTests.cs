using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FamilyOnboardingDraftingServiceTests
{
    private IProductFamilyStore _families = null!;
    private IJobStore _jobs = null!;
    private IFamilySampleFetchService _sampleFetch = null!;
    private IFamilyDraftClient _draftClient = null!;
    private IFamilyOnboardingStore _onboarding = null!;
    private FamilyOnboardingDraftingService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _families = Substitute.For<IProductFamilyStore>();
        _jobs = Substitute.For<IJobStore>();
        _sampleFetch = Substitute.For<IFamilySampleFetchService>();
        _draftClient = Substitute.For<IFamilyDraftClient>();
        _onboarding = Substitute.For<IFamilyOnboardingStore>();
        _service = new FamilyOnboardingDraftingService(_families, _jobs, _sampleFetch, _draftClient, _onboarding);
    }

    [Test]
    public async Task Should_draft_a_new_family_end_to_end_and_apply_the_proposed_deal_grouping()
    {
        var draftFamily = BuildFamily(state: FamilyState.Draft);
        _families.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", Arg.Any<CancellationToken>(), FamilyState.Draft)
            .Returns(draftFamily);
        var job = BuildJob();
        _jobs.CreateJob(Arg.Any<JobDetails>(), Arg.Any<CancellationToken>()).Returns(job);
        var sample = new[] { new FamilySampleListing("m1", "Title", "Desc", null, null, false, 20m, null) };
        _sampleFetch.FetchSample("ps5 controller", Arg.Any<CancellationToken>()).Returns(sample);
        var draftResult = new FamilyDraftResult("{\"family\":\"ps5-controller\"}", "edition,colour", 100, 200, 0.02m);
        _draftClient.DraftTaxonomy(Arg.Any<FamilyDraftPrompt>(), Arg.Any<CancellationToken>()).Returns(draftResult);
        var activatedView = BuildFamily(state: FamilyState.Draft);
        _families.GetFamily(draftFamily.Id, Arg.Any<CancellationToken>()).Returns(activatedView);

        var result = await _service.StartOnboarding("PS5 Controller", "ps5 controller", "ps5-controller", CancellationToken.None);

        Assert.That(result, Is.SameAs(activatedView));
        var sentJobDetails = (JobDetails)_jobs.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(sentJobDetails.SearchTerm, Is.EqualTo("ps5 controller"));
            Assert.That(sentJobDetails.IsEnabled, Is.False);
        });
        await _families.Received(1).SetJobFamily(job.Id, draftFamily.Id, Arg.Any<CancellationToken>());
        await _families.Received(1).AddTaxonomyVersion(draftFamily.Id, draftResult.TaxonomyJson, Arg.Any<CancellationToken>());
        await _onboarding.Received(1).Create(
            draftFamily.Id, job.Id, "ps5 controller", sample, 100, 200, 0.02m, Arg.Any<CancellationToken>());
        await _families.Received(1).UpdateFamily(
            draftFamily.Id, null, null, "edition,colour", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_slugify_the_name_into_a_key_when_none_is_supplied()
    {
        _families.CreateFamily(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), FamilyState.Draft)
            .Returns((ProductFamilyView?)null);

        await _service.StartOnboarding("PS5 Controller!!", "ps5 controller", null, CancellationToken.None);

        await _families.Received(1).CreateFamily(
            "ps5-controller", "PS5 Controller!!", "ps5-controller", Arg.Any<CancellationToken>(), FamilyState.Draft);
    }

    [Test]
    public async Task Should_return_null_and_do_nothing_further_when_family_creation_conflicts()
    {
        _families.CreateFamily(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), FamilyState.Draft)
            .Returns((ProductFamilyView?)null);

        var result = await _service.StartOnboarding("PS5 Controller", "ps5 controller", "ps5-controller", CancellationToken.None);

        Assert.That(result, Is.Null);
        await _jobs.DidNotReceive().CreateJob(Arg.Any<JobDetails>(), Arg.Any<CancellationToken>());
        await _sampleFetch.DidNotReceive().FetchSample(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_return_null_when_regenerating_a_family_that_is_not_in_draft_state()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily(state: FamilyState.Active));

        var result = await _service.Regenerate(1, "tighten the criteria", CancellationToken.None);

        Assert.That(result, Is.Null);
        await _draftClient.DidNotReceive().DraftTaxonomy(Arg.Any<FamilyDraftPrompt>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_return_null_when_regenerating_without_an_onboarding_snapshot()
    {
        _families.GetFamily(1, Arg.Any<CancellationToken>()).Returns(BuildFamily(state: FamilyState.Draft));
        _onboarding.GetSnapshot(1, Arg.Any<CancellationToken>()).Returns((FamilyOnboardingSnapshot?)null);

        var result = await _service.Regenerate(1, "tighten the criteria", CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Should_redraft_with_feedback_and_the_previous_taxonomy_as_context()
    {
        var family = BuildFamily(state: FamilyState.Draft);
        _families.GetFamily(family.Id, Arg.Any<CancellationToken>()).Returns(family);
        var sample = new[] { new FamilySampleListing("m1", "Title", null, null, null, false, null, null) };
        var snapshot = new FamilyOnboardingSnapshot(
            family.Id, 10, "ps5 controller", sample, [], 100, 200, 0.02m, null, DateTime.UtcNow);
        _onboarding.GetSnapshot(family.Id, Arg.Any<CancellationToken>()).Returns(snapshot);
        var draftResult = new FamilyDraftResult("{\"family\":\"ps5-controller\"}", null, 30, 40, 0.005m);
        _draftClient.DraftTaxonomy(Arg.Any<FamilyDraftPrompt>(), Arg.Any<CancellationToken>()).Returns(draftResult);

        await _service.Regenerate(family.Id, "tighten the criteria", CancellationToken.None);

        var sentPrompt = (FamilyDraftPrompt)_draftClient.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(sentPrompt.Feedback, Is.EqualTo("tighten the criteria"));
            Assert.That(sentPrompt.PreviousTaxonomyJson, Is.EqualTo(family.LatestTaxonomyVersion!.QuestionsJson));
            Assert.That(sentPrompt.Sample, Is.SameAs(sample));
        });
        await _families.Received(1).AddTaxonomyVersion(family.Id, draftResult.TaxonomyJson, Arg.Any<CancellationToken>());
        await _onboarding.Received(1).RecordRedraft(
            family.Id, "tighten the criteria", 30, 40, 0.005m, Arg.Any<CancellationToken>());
        await _families.DidNotReceive().UpdateFamily(
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    private static JobView BuildJob() =>
        new(10, "ps5 controller", Marketplace.Mercari, null, 24, false, null, null, DateTime.UtcNow, [], null);

    private static ProductFamilyView BuildFamily(FamilyState state) =>
        new(
            1,
            "ps5-controller",
            "PS5 Controller",
            "ps5-controller",
            DateTime.UtcNow,
            new TaxonomyVersionView(100, 1, 1, "{\"family\":\"ps5-controller\"}", DateTime.UtcNow),
            State: state);
}
