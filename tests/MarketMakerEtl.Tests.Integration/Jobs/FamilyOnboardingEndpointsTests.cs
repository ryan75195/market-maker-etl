using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class FamilyOnboardingEndpointsTests : JobsApiTestBase
{
    private const string DraftTaxonomyJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": {
            "ps5_controller": "A PS5 DualSense controller.",
            "other": "Anything else."
           }
          }
         }
        }
        """;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IFamilyDraftClient>();
        services.AddSingleton(BuildDraftClientStub());

        services.RemoveAll<IFamilySampleFetchService>();
        services.AddSingleton(BuildSampleFetchStub());

        services.RemoveAll<IListingClassifierClient>();
        services.AddSingleton(BuildClassifierStub());
    }

    [Test]
    public async Task Should_onboard_a_family_and_return_a_draft_taxonomy_with_preview()
    {
        var response = await StartOnboardingResponse();
        var onboarding = await response.Content.ReadFromJsonAsync<FamilyOnboardingView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(onboarding!.State, Is.EqualTo(FamilyState.Draft));
            Assert.That(onboarding.TaxonomyJson, Is.EqualTo(DraftTaxonomyJson));
            Assert.That(onboarding.SampleSize, Is.EqualTo(2));
            Assert.That(onboarding.Preview, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Should_get_onboarding_state_for_a_draft_family()
    {
        var onboarding = await StartOnboarding();

        var response = await Client.GetAsync($"/api/families/{onboarding.FamilyId}/onboarding");
        var fetched = await response.Content.ReadFromJsonAsync<FamilyOnboardingView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fetched!.FamilyId, Is.EqualTo(onboarding.FamilyId));
        });
    }

    [Test]
    public async Task Should_return_not_found_for_onboarding_of_an_unknown_family()
    {
        var response = await Client.GetAsync("/api/families/999999/onboarding");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Should_regenerate_a_draft_taxonomy_with_feedback()
    {
        var onboarding = await StartOnboarding();

        var response = await Client.PostAsJsonAsync(
            $"/api/families/{onboarding.FamilyId}/onboarding/regenerate",
            new RegenerateOnboardingRequest("Add a condition question."));
        var regenerated = await response.Content.ReadFromJsonAsync<FamilyOnboardingView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(regenerated!.LastFeedback, Is.EqualTo("Add a condition question."));
        });
    }

    [Test]
    public async Task Should_reject_a_draft_family_without_making_it_active()
    {
        var onboarding = await StartOnboarding();

        var response = await Client.PostAsync($"/api/families/{onboarding.FamilyId}/onboarding/reject", null);
        var getAfterReject = await Client.GetAsync($"/api/families/{onboarding.FamilyId}");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(getAfterReject.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Should_approve_a_draft_family_and_make_it_active_with_an_enabled_job()
    {
        var onboarding = await StartOnboarding();

        var response = await Client.PostAsync($"/api/families/{onboarding.FamilyId}/onboarding/approve", null);
        var approved = await response.Content.ReadFromJsonAsync<ProductFamilyView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(approved!.State, Is.EqualTo(FamilyState.Active));
        });
    }

    [Test]
    public async Task Should_update_deal_settings_for_a_draft_family()
    {
        var onboarding = await StartOnboarding();

        var response = await Client.PutAsJsonAsync(
            $"/api/families/{onboarding.FamilyId}/onboarding/deal-settings",
            new UpdateOnboardingDealSettingsRequest("item_type", 0.3m, 3));
        var updated = await response.Content.ReadFromJsonAsync<ProductFamilyView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated!.DealGroupBy, Is.EqualTo("item_type"));
            Assert.That(updated.DealMinSold, Is.EqualTo(3));
        });
    }

    private async Task<HttpResponseMessage> StartOnboardingResponse() =>
        await Client.PostAsJsonAsync(
            "/api/families/onboard", new StartOnboardingRequest("PS5 Controller", "ps5 controller"));

    private async Task<FamilyOnboardingView> StartOnboarding()
    {
        var response = await StartOnboardingResponse();
        return (await response.Content.ReadFromJsonAsync<FamilyOnboardingView>())!;
    }

    private static IFamilyDraftClient BuildDraftClientStub()
    {
        var stub = Substitute.For<IFamilyDraftClient>();
        stub.DraftTaxonomy(Arg.Any<FamilyDraftPrompt>(), Arg.Any<CancellationToken>())
            .Returns(new FamilyDraftResult(DraftTaxonomyJson, null, 100, 50, 0.01m));
        return stub;
    }

    private static IFamilySampleFetchService BuildSampleFetchStub()
    {
        var stub = Substitute.For<IFamilySampleFetchService>();
        stub.FetchSample(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<FamilySampleListing>
            {
                new("m1", "PS5 controller", "Great condition", "Electronics", "Sony", false, 40m, "https://example.com/1"),
                new("m2", "Xbox controller", "For parts", "Electronics", "Microsoft", true, 15m, "https://example.com/2")
            });
        return stub;
    }

    private static IListingClassifierClient BuildClassifierStub()
    {
        var stub = Substitute.For<IListingClassifierClient>();
        stub.Classify(Arg.Any<ClassifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => BuildClassifyResponse(callInfo.Arg<ClassifyRequest>()));
        return stub;
    }

    private static ClassifyResponse BuildClassifyResponse(ClassifyRequest request)
    {
        var results = request.States
            .Select(state => new ClassifyResult(new Dictionary<string, ClassifyAnswer>
            {
                ["item_type"] = new(
                    state.Id == "m1" ? "ps5_controller" : "other", 0.9, 0.9, new Dictionary<string, double>())
            }))
            .ToList();
        return new ClassifyResponse(request.Model, 1, results);
    }
}
