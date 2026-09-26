using System.Net.Http.Json;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class JobWithoutMarketplaceDefaultsToMercariTests : JobsApiTestBase
{
    [Test]
    public async Task Should_default_a_job_created_without_a_marketplace_to_mercari()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new { searchTerm = "gundam figure" });
        var created = await createResponse.Content.ReadFromJsonAsync<JobView>(TestJsonOptions.Default);

        Assert.That(created!.Marketplace, Is.EqualTo(Marketplace.Mercari));
    }

    [Test]
    public async Task Should_leave_the_legacy_scrape_jobs_endpoint_defaulting_to_ebay()
    {
        await Client.PostAsJsonAsync("/api/scrape/jobs", new { searchTerm = "gundam figure legacy" });

        var jobs = await Client.GetFromJsonAsync<List<JobView>>("/api/jobs", TestJsonOptions.Default);
        var legacyJob = jobs!.Single(j => j.SearchTerm == "gundam figure legacy");

        Assert.That(legacyJob.Marketplace, Is.EqualTo(Marketplace.Ebay));
    }
}
