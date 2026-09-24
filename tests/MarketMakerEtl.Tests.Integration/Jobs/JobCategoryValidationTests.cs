using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobCategoryValidationTests : JobsApiTestBase
{
    [Test]
    public async Task Should_reject_creating_a_job_with_an_unknown_category_id()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest("labubu", CategoryIds: [999]));

        var jobsAfter = await Client.GetFromJsonAsync<List<JobView>>("/api/jobs");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(jobsAfter, Is.Empty);
        });
    }

    [Test]
    public async Task Should_accept_creating_a_job_when_every_category_id_exists()
    {
        var categoryResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Toys"));
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryView>();

        var response = await Client.PostAsJsonAsync(
            "/api/jobs",
            new CreateJobRequest("labubu", CategoryIds: [category!.Id]));
        var created = await response.Content.ReadFromJsonAsync<JobView>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created!.Categories.Select(c => c.Id), Is.EquivalentTo(new[] { category.Id }));
        });
    }

    [Test]
    public async Task Should_reject_updating_a_job_with_an_unknown_category_id()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("labubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>();

        var updateResponse = await Client.PutAsJsonAsync(
            $"/api/jobs/{created!.Id}",
            new UpdateJobRequest("labubu", created.Marketplace, null, created.IntervalHours, true, [999]));

        var reread = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{created.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reread!.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task Should_reject_setting_an_unknown_category_id_on_a_job()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("labubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>();

        var setResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{created!.Id}/categories",
            new SetJobCategoriesRequest([999]));

        var reread = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{created.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(setResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reread!.Categories, Is.Empty);
        });
    }
}
