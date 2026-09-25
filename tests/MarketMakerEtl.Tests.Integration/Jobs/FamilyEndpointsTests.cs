using System.Net;
using System.Net.Http.Json;
using System.Text;
using MarketMakerEtl.Api;
using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Tests.Integration.Jobs;

[TestFixture]
public class FamilyEndpointsTests : JobsApiTestBase
{
    private const string ValidTaxonomyJson = """
        {
         "family": "test-family",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": {
            "widget": "A widget.",
            "gadget": "A gadget."
           }
          }
         }
        }
        """;

    private const string InvalidTaxonomyJson = """
        {
         "family": "test-family",
         "version": 1,
         "questions": {}
        }
        """;

    [Test]
    public async Task Should_create_and_list_families()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest("ps5-controller", "PS5 Controller", "ps5-controller"));
        var created = await createResponse.Content.ReadFromJsonAsync<ProductFamilyView>();

        var listResponse = await Client.GetFromJsonAsync<List<ProductFamilyView>>("/api/families");
        var readResponse = await Client.GetFromJsonAsync<ProductFamilyView>($"/api/families/{created!.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(created.Key, Is.EqualTo("ps5-controller"));
            Assert.That(created.Name, Is.EqualTo("PS5 Controller"));
            Assert.That(created.ModelName, Is.EqualTo("ps5-controller"));
            Assert.That(created.LatestTaxonomyVersion, Is.Null);
            Assert.That(listResponse!.Select(f => f.Id), Does.Contain(created.Id));
            Assert.That(readResponse!.Key, Is.EqualTo("ps5-controller"));
        });
    }

    [Test]
    public async Task Should_post_a_valid_taxonomy_and_increment_version_numbers()
    {
        var family = await CreateFamily("iphone-15");

        var firstResponse = await PostTaxonomy(family.Id, ValidTaxonomyJson);
        var first = await firstResponse.Content.ReadFromJsonAsync<TaxonomyVersionView>();

        var secondResponse = await PostTaxonomy(family.Id, ValidTaxonomyJson);
        var second = await secondResponse.Content.ReadFromJsonAsync<TaxonomyVersionView>();

        var latestFamily = await Client.GetFromJsonAsync<ProductFamilyView>($"/api/families/{family.Id}");
        var readBack = await Client.GetFromJsonAsync<TaxonomyVersionView>(
            $"/api/families/{family.Id}/taxonomies/{first!.Version}");

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(first.Version, Is.EqualTo(1));
            Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(second!.Version, Is.EqualTo(2));
            Assert.That(latestFamily!.LatestTaxonomyVersion!.Version, Is.EqualTo(2));
            Assert.That(readBack!.QuestionsJson, Is.EqualTo(ValidTaxonomyJson));
        });
    }

    [Test]
    public async Task Should_reject_an_invalid_taxonomy_with_validation_errors()
    {
        var family = await CreateFamily("empty-questions");

        var response = await PostTaxonomy(family.Id, InvalidTaxonomyJson);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("questions"));
        });
    }

    [Test]
    public async Task Should_reject_creating_a_family_with_a_duplicate_key()
    {
        await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest("duplicate-key", "First", "first-model"));

        var response = await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest("duplicate-key", "Second", "second-model"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [TestCase("not json at all", TestName = "Should_reject_a_syntactically_invalid_body")]
    [TestCase("""["not", "an", "object"]""", TestName = "Should_reject_a_non_object_body")]
    [TestCase(
        """{ "family": "x", "version": 1, "questions": "not-an-object" }""",
        TestName = "Should_reject_questions_that_is_not_an_object")]
    [TestCase(
        """{ "family": "x", "version": 1, "questions": { "item_type": "not-an-object" } }""",
        TestName = "Should_reject_a_question_that_is_not_an_object")]
    [TestCase(
        """
        {
         "family": "x",
         "version": 1,
         "questions": {
          "item_type": { "instructions": "What is this?", "criteria": ["a", "b"] }
         }
        }
        """,
        TestName = "Should_reject_criteria_that_is_not_an_object")]
    [TestCase(
        """
        {
         "family": "x",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": { "a": "A.", "b": "B." },
           "askWhen": "not-an-array"
          }
         }
        }
        """,
        TestName = "Should_reject_ask_when_that_is_not_an_array")]
    public async Task Should_reject_a_malformed_taxonomy_body_with_400(string malformedJson)
    {
        var family = await CreateFamily(Guid.NewGuid().ToString("N"));

        var response = await PostTaxonomy(family.Id, malformedJson);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Should_return_not_found_for_unknown_family_and_taxonomy_version()
    {
        var family = await CreateFamily("unknown-lookups");

        var unknownFamilyResponse = await Client.GetAsync("/api/families/999999");
        var unknownVersionResponse = await Client.GetAsync($"/api/families/{family.Id}/taxonomies/1");
        var unknownTaxonomyTargetResponse = await PostTaxonomy(999999, ValidTaxonomyJson);

        Assert.Multiple(() =>
        {
            Assert.That(unknownFamilyResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknownVersionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknownTaxonomyTargetResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    private async Task<ProductFamilyView> CreateFamily(string key)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/families",
            new CreateProductFamilyRequest(key, key, key));
        return (await response.Content.ReadFromJsonAsync<ProductFamilyView>())!;
    }

    private Task<HttpResponseMessage> PostTaxonomy(int familyId, string questionsJson)
    {
        var content = new StringContent(questionsJson, Encoding.UTF8, "application/json");
        return Client.PostAsync($"/api/families/{familyId}/taxonomies", content);
    }
}
