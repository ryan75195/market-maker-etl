using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobCrudEndpointsTests : JobsApiTestBase
{
    [Test]
    public async Task Should_create_list_read_update_disable_enable_and_delete_a_job()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest("nintendo switch oled", Marketplace.Mercari, "OLED model only", 12, true, null));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var listResponse = await Client.GetFromJsonAsync<List<JobView>>("/api/jobs", TestJsonOptions.Default);
        var readResponse = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{created!.Id}", TestJsonOptions.Default);

        var updateResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{created.Id}",
            new UpdateJobRequest("nintendo switch v2", Marketplace.Mercari, "Any model", 6, true, null));
        var updated = await updateResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var disableResponse = await Client.PostAsync($"/api/jobs/{created.Id}/disable", null);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var enableResponse = await Client.PostAsync($"/api/jobs/{created.Id}/enable", null);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        var deleteResponse = await Client.DeleteAsync($"/api/jobs/{created.Id}");
        var afterDeleteResponse = await Client.GetAsync($"/api/jobs/{created.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created.SearchTerm, Is.EqualTo("nintendo switch oled"));
            Assert.That(created.Marketplace, Is.EqualTo(Marketplace.Mercari));
            Assert.That(created.FilterInstructions, Is.EqualTo("OLED model only"));
            Assert.That(created.IntervalHours, Is.EqualTo(12));
            Assert.That(created.IsEnabled, Is.True);
            Assert.That(created.LastQueuedUtc, Is.Null);
            Assert.That(created.LastRunUtc, Is.Null);
            Assert.That(created.Categories, Is.Empty);

            Assert.That(listResponse!.Select(j => j.Id), Does.Contain(created.Id));
            Assert.That(readResponse!.SearchTerm, Is.EqualTo("nintendo switch oled"));

            Assert.That(updated!.SearchTerm, Is.EqualTo("nintendo switch v2"));
            Assert.That(updated.FilterInstructions, Is.EqualTo("Any model"));
            Assert.That(updated.IntervalHours, Is.EqualTo(6));

            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(enabled!.IsEnabled, Is.True);

            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterDeleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
