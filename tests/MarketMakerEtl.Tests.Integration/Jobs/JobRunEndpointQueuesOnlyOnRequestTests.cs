using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobRunEndpointQueuesOnlyOnRequestTests : JobsApiTestBase
{
    [Test]
    public async Task Should_queue_a_run_only_when_the_run_endpoint_is_called()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("labubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var runBeforeQueueing = await Client.GetAsync("/api/scrape/runs/1");

        var runResponse = await Client.PostAsync($"/api/jobs/{created!.Id}/run", null);
        var runBody = await runResponse.Content.ReadFromJsonAsync<EnqueueRunResponse>();

        var queuedRun = await Client.GetFromJsonAsync<ScrapeRunView>(
            $"/api/scrape/runs/{runBody!.RunId}", TestJsonOptions.Default);
        var jobAfterRun = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{created.Id}", TestJsonOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(created.LastQueuedUtc, Is.Null);
            Assert.That(runBeforeQueueing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            Assert.That(runResponse.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(queuedRun!.JobId, Is.EqualTo(created.Id));
            Assert.That(queuedRun.Status, Is.EqualTo(ScrapeRunStatus.Queued));
            Assert.That(queuedRun.TriggerType, Is.EqualTo(TriggerType.Manual));
            Assert.That(jobAfterRun!.LastQueuedUtc, Is.Not.Null);
        });
    }
}
