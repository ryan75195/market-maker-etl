using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobFamilyAssignmentEndpointTests : JobsApiTestBase
{
    [Test]
    public async Task Should_assign_and_clear_a_jobs_family()
    {
        var jobResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("ps5 controller"));
        var job = await jobResponse.Content.ReadFromJsonAsync<JobView>();

        var familyResponse = await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest("ps5-controller", "PS5 Controller", "ps5-controller"));
        var family = await familyResponse.Content.ReadFromJsonAsync<ProductFamilyView>();

        var assignResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{job!.Id}/family",
            new SetJobFamilyRequest(family!.Id));
        var assigned = await assignResponse.Content.ReadFromJsonAsync<JobView>();

        var clearResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{job.Id}/family",
            new SetJobFamilyRequest(null));
        var cleared = await clearResponse.Content.ReadFromJsonAsync<JobView>();

        Assert.Multiple(() =>
        {
            Assert.That(assignResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(assigned!.ProductFamilyId, Is.EqualTo(family.Id));
            Assert.That(clearResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(cleared!.ProductFamilyId, Is.Null);
        });
    }

    [Test]
    public async Task Should_return_not_found_when_assigning_an_unknown_job_or_family()
    {
        var jobResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("ps5 controller"));
        var job = await jobResponse.Content.ReadFromJsonAsync<JobView>();

        var unknownJobResponse = await Client.PutAsJsonAsync(
            "/api/jobs/999999/family",
            new SetJobFamilyRequest(null));
        var unknownFamilyResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{job!.Id}/family",
            new SetJobFamilyRequest(999999));

        Assert.Multiple(() =>
        {
            Assert.That(unknownJobResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknownFamilyResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
