using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiListingClassifierClientTests
{
    private const string SuccessBody = """
        {
          "labels": [
            { "id": "1", "ambiguous": "", "item_type": "console" }
          ]
        }
        """;

    private static OpenAiOptions Options(int batchSize = 25, int maxConcurrency = 1, int timeoutSeconds = 30) =>
        new("test-key", "gpt-6-luna", "low", batchSize, maxConcurrency, timeoutSeconds);

    private static ClassificationReviewOptions ReviewOptions() => new(0.9);

    private static IOpenAiChatCompletionSender CreateSender()
    {
        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(false);
        var pricing = new OpenAiPricingOptions(
            new Dictionary<string, OpenAiModelPricing>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-6-luna"] = new OpenAiModelPricing(0.10m, 0.50m)
            },
            new OpenAiModelPricing(2.0m, 10.0m));
        return new OpenAiChatCompletionSender(TimeProvider.System, budget, Substitute.For<IOpenAiUsageStore>(), pricing);
    }

    [Test]
    public async Task Should_post_a_taxonomy_derived_system_prompt_and_strict_json_schema()
    {
        var handler = new StubHandler(_ => Json(SuccessBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var root = document.RootElement;
        var systemMessage = root.GetProperty("messages")[0].GetProperty("content").GetString();
        var schema = root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
        var itemSchema = schema.GetProperty("properties").GetProperty("labels").GetProperty("items");
        var enumValues = itemSchema.GetProperty("properties").GetProperty("item_type").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("gpt-6-luna"));
            Assert.That(root.GetProperty("reasoning_effort").GetString(), Is.EqualTo("low"));
            Assert.That(systemMessage, Does.Contain("What is this?"));
            Assert.That(systemMessage, Does.Contain("A console."));
            Assert.That(itemSchema.GetProperty("properties").TryGetProperty("ambiguous", out _), Is.True);
            Assert.That(enumValues, Does.Contain("console"));
            Assert.That(enumValues, Does.Contain("not_applicable"));
        });
    }

    [Test]
    public async Task Should_include_guidance_in_the_system_prompt_when_present()
    {
        var handler = new StubHandler(_ => Json(SuccessBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        await client.Classify(BuildRequest(guidance: "Treat iPhone SE as base model."), OpenAiUsagePurpose.Classification, CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var systemMessage = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.That(systemMessage, Does.Contain("Treat iPhone SE as base model."));
    }

    [Test]
    public async Task Should_map_a_successful_response_into_classify_results()
    {
        var handler = new StubHandler(_ => Json(SuccessBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        var result = response.Results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Answers["item_type"].Choice, Is.EqualTo("console"));
            Assert.That(result.Answers["item_type"].Confidence, Is.EqualTo(1.0));
            Assert.That(result.Answers["item_type"].Agreement, Is.EqualTo(1.0));
        });
    }

    [Test]
    public async Task Should_batch_requests_and_return_results_in_original_order()
    {
        var handler = new StubHandler(request =>
        {
            using var body = JsonDocument.Parse(request);
            var ids = ExtractIds(body);
            var labels = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"ambiguous\":\"\",\"item_type\":\"console\"}}"));
            return Json($$"""{"labels":[{{labels}}]}""");
        });
        var client = new OpenAiListingClassifierClient(
            new HttpClient(handler), Options(batchSize: 2, maxConcurrency: 1), ReviewOptions(), CreateSender());
        var states = new[]
        {
            new ClassifyListingState("1", "First", null, null, null, false),
            new ClassifyListingState("2", "Second", null, null, null, false),
            new ClassifyListingState("3", "Third", null, null, null, false)
        };

        var response = await client.Classify(BuildRequest(states), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestBodies, Has.Count.EqualTo(2));
            Assert.That(response.Results, Has.Count.EqualTo(3));
            Assert.That(response.Results.Select(r => r.Answers["item_type"].Choice), Is.All.EqualTo("console"));
        });
    }

    [Test]
    public async Task Should_treat_a_missing_listing_in_the_response_as_a_failure_for_that_listing_only()
    {
        const string partialBody = """
            {
              "labels": [
                { "id": "1", "ambiguous": "", "item_type": "console" }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(partialBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());
        var states = new[]
        {
            new ClassifyListingState("1", "First", null, null, null, false),
            new ClassifyListingState("2", "Second", null, null, null, false)
        };

        var response = await client.Classify(BuildRequest(states), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Results[0].Error, Is.Null);
            Assert.That(response.Results[1].Error, Is.Not.Null);
        });
    }

    [Test]
    public async Task Should_retry_on_429_and_then_succeed()
    {
        var attempt = 0;
        var handler = new StubHandler(_ =>
        {
            attempt++;
            return attempt == 1
                ? new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"error\":\"rate limited\"}", Encoding.UTF8, "application/json")
                }
                : Json(SuccessBody);
        });
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(attempt, Is.EqualTo(2));
            Assert.That(response.Results.Single().Answers["item_type"].Choice, Is.EqualTo("console"));
        });
    }

    [Test]
    public async Task Should_report_a_batch_wide_failure_when_all_retries_are_exhausted()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"boom\"}", Encoding.UTF8, "application/json")
        });
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.That(response.Results.Single().Error, Is.Not.Null);
    }

    [Test]
    public async Task Should_lower_confidence_for_the_named_question_when_a_listing_is_ambiguous()
    {
        const string ambiguousBody = """
            {
              "labels": [
                { "id": "1", "ambiguous": "unsure about item_type", "item_type": "console" }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(ambiguousBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.That(response.Results.Single().Answers["item_type"].Confidence, Is.LessThan(0.9));
    }

    [Test]
    public async Task Should_lower_confidence_for_all_questions_when_the_ambiguous_note_does_not_name_a_question()
    {
        const string ambiguousBody = """
            {
              "labels": [
                { "id": "1", "ambiguous": "hard to tell from the photo", "item_type": "console" }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(ambiguousBody));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest(), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.That(response.Results.Single().Answers["item_type"].Confidence, Is.LessThan(0.9));
    }

    [Test]
    public async Task Should_return_empty_results_without_calling_out_for_an_empty_batch()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not be called"));
        var client = new OpenAiListingClassifierClient(new HttpClient(handler), Options(), ReviewOptions(), CreateSender());

        var response = await client.Classify(BuildRequest([]), OpenAiUsagePurpose.Classification, CancellationToken.None);

        Assert.That(response.Results, Is.Empty);
    }

    private static IReadOnlyList<string> ExtractIds(JsonDocument body)
    {
        var userMessage = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        var jsonStart = userMessage.IndexOf('\n', StringComparison.Ordinal) + 1;
        using var listings = JsonDocument.Parse(userMessage[jsonStart..]);
        return listings.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
    }

    private static ClassifyRequest BuildRequest(string? guidance = null) =>
        BuildRequest([new ClassifyListingState("1", "Test title", null, null, null, false)], guidance);

    private static ClassifyRequest BuildRequest(IReadOnlyList<ClassifyListingState> states, string? guidance = null) =>
        new(
            "ps5-controller",
            new Dictionary<string, ClassifyQuestion>
            {
                ["item_type"] = new(
                    "choice",
                    "What is this?",
                    new Dictionary<string, string> { ["console"] = "A console.", ["not_applicable"] = "Not applicable." })
            },
            states,
            guidance);

    private static HttpResponseMessage Json(string labelsJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(WrapAsChatCompletion(labelsJson), Encoding.UTF8, "application/json")
    };

    private static string WrapAsChatCompletion(string content) =>
        new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject { ["message"] = new JsonObject { ["content"] = content } }
            }
        }.ToJsonString();

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        private readonly List<string> _requestBodies = [];

        public StubHandler(Func<string, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public IReadOnlyList<string> RequestBodies => _requestBodies;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requestBodies)
            {
                _requestBodies.Add(body);
            }

            return _respond(body);
        }
    }
}
