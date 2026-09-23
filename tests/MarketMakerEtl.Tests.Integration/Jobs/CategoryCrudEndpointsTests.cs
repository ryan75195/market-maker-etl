using System.Net;
using System.Net.Http.Json;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class CategoryCrudEndpointsTests : JobsApiTestBase
{
    [Test]
    public async Task Should_create_list_read_update_disable_enable_and_delete_a_category()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Consoles"));
        var created = await createResponse.Content.ReadFromJsonAsync<CategoryView>();

        var listResponse = await Client.GetFromJsonAsync<List<CategoryView>>("/api/categories");
        var readResponse = await Client.GetFromJsonAsync<CategoryView>($"/api/categories/{created!.Id}");

        var updateResponse = await Client.PutAsJsonAsync(
            $"/api/categories/{created.Id}",
            new UpdateCategoryRequest("Retro Consoles", true));
        var updated = await updateResponse.Content.ReadFromJsonAsync<CategoryView>();

        var disableResponse = await Client.PostAsync($"/api/categories/{created.Id}/disable", null);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<CategoryView>();

        var enableResponse = await Client.PostAsync($"/api/categories/{created.Id}/enable", null);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<CategoryView>();

        var deleteResponse = await Client.DeleteAsync($"/api/categories/{created.Id}");
        var afterDeleteResponse = await Client.GetAsync($"/api/categories/{created.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created.Name, Is.EqualTo("Consoles"));
            Assert.That(created.IsEnabled, Is.True);

            Assert.That(listResponse!.Select(c => c.Id), Does.Contain(created.Id));
            Assert.That(readResponse!.Name, Is.EqualTo("Consoles"));

            Assert.That(updated!.Name, Is.EqualTo("Retro Consoles"));

            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(enabled!.IsEnabled, Is.True);

            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterDeleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
