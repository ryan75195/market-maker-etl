using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobRunsEndpointReturnsCountersAndIssuesTests : JobsApiTestBase
{
    [Test]
    public async Task Should_return_recent_runs_for_the_job_with_their_trigger_and_counters()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("labubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var runResponse = await Client.PostAsync($"/api/jobs/{created!.Id}/run", null);
        var runBody = await runResponse.Content.ReadFromJsonAsync<EnqueueRunResponse>();

        var runsResponse = await Client.GetAsync($"/api/jobs/{created.Id}/runs");
        var runs = await runsResponse.Content.ReadFromJsonAsync<List<ScrapeRunView>>(TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(runsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(runs, Has.Count.EqualTo(1));
            Assert.That(runs![0].RunId, Is.EqualTo(runBody!.RunId));
            Assert.That(runs[0].TriggerType, Is.EqualTo(TriggerType.Manual));
            Assert.That(runs[0].Status, Is.EqualTo(ScrapeRunStatus.Queued));
            Assert.That(runs[0].ListingsAddedActive, Is.EqualTo(0));
            Assert.That(runs[0].Issues, Is.Empty);
        });
    }

    [Test]
    public async Task Should_return_not_found_for_an_unknown_job()
    {
        var response = await Client.GetAsync("/api/jobs/999999/runs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
