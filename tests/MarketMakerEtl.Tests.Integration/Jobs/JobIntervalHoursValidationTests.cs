using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobIntervalHoursValidationTests : JobsApiTestBase
{
    [Test]
    public async Task Should_reject_creating_a_job_with_interval_hours_below_one()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest("labubu", IntervalHours: 0));

        var jobsAfter = await Client.GetFromJsonAsync<List<JobView>>("/api/jobs");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(jobsAfter, Is.Empty);
        });
    }

    [Test]
    public async Task Should_accept_creating_a_job_with_interval_hours_of_one()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest("labubu", IntervalHours: 1));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task Should_reject_updating_a_job_with_interval_hours_below_one()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("labubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>();

        var updateResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{created!.Id}",
            new UpdateJobRequest("labubu", created.Marketplace, null, -1, true, null));

        var reread = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{created.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reread!.IntervalHours, Is.EqualTo(created.IntervalHours));
        });
    }
}
