using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobCategoryAssignmentEndpointTests : JobsApiTestBase
{
    [Test]
    public async Task Should_assign_categories_to_a_job_and_return_them_on_the_job()
    {
        var jobResponse = await Client.PostAsJsonAsync("/api/jobs", new CreateJobRequest("amiibo figures"));
        var job = await jobResponse.Content.ReadFromJsonAsync<JobView>();

        var electronicsResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Electronics"));
        var electronics = await electronicsResponse.Content.ReadFromJsonAsync<CategoryView>();
        var toysResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Toys"));
        var toys = await toysResponse.Content.ReadFromJsonAsync<CategoryView>();

        var assignResponse = await Client.PostAsJsonAsync(
            $"/api/jobs/{job!.Id}/categories",
            new SetJobCategoriesRequest([electronics!.Id, toys!.Id]));
        var assigned = await assignResponse.Content.ReadFromJsonAsync<JobView>();

        var reread = await Client.GetFromJsonAsync<JobView>($"/api/jobs/{job.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(assigned!.Categories.Select(c => c.Id), Is.EquivalentTo(new[] { electronics.Id, toys.Id }));
            Assert.That(reread!.Categories.Select(c => c.Name), Is.EquivalentTo(new[] { "Electronics", "Toys" }));
        });
    }
}
