using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiFamilyDraftClientTests
{
    private const string ValidTaxonomyContent =
        """{"family":"ps5-controller","version":1,"questions":{"item_type":{"instructions":"What is this?","criteria":{"controller":"A controller.","other":"Something else."}},"edition":{"instructions":"Which edition?","criteria":{"standard":"Standard.","not_stated":"Unknown."},"askWhen":[{"question":"item_type","anyOf":["controller"]}]}},"dealGroupBy":"edition"}""";

    private const string UngatedTaxonomyContent =
        """{"family":"ps5-controller","version":1,"questions":{"item_type":{"instructions":"What is this?","criteria":{"controller":"A controller.","other":"Something else."}}}}""";

    private static OpenAiOptions Options() => new("test-key", "gpt-6-luna", "low", 25, 6, 30);

    private static OnboardingOptions DraftOptions() =>
        new("gpt-6-sol", "medium", 60, 500, 180);

    private static FamilyDraftPrompt Prompt() =>
        new("PS5 Controller", "ps5 controller", [], null, null);

    private static IOpenAiChatCompletionSender CreateSender()
    {
        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(false);
        var pricing = new OpenAiPricingOptions(
            new Dictionary<string, OpenAiModelPricing>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-6-sol"] = new OpenAiModelPricing(2.0m, 10.0m)
            },
            new OpenAiModelPricing(2.0m, 10.0m));
        return new OpenAiChatCompletionSender(TimeProvider.System, budget, Substitute.For<IOpenAiUsageStore>(), pricing);
    }

    [Test]
    public async Task Should_draft_a_taxonomy_and_compute_cost_from_token_usage()
    {
        var handler = new StubHandler(_ => SuccessResponse(ValidTaxonomyContent, 120, 340));
        var client = new OpenAiFamilyDraftClient(new HttpClient(handler), Options(), DraftOptions(), CreateSender(), NullLogger<OpenAiFamilyDraftClient>.Instance);

        var result = await client.DraftTaxonomy(Prompt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TaxonomyJson, Is.EqualTo(ValidTaxonomyContent));
            Assert.That(result.DealGroupBy, Is.EqualTo("edition"));
            Assert.That(result.PromptTokens, Is.EqualTo(120));
            Assert.That(result.CompletionTokens, Is.EqualTo(340));
            Assert.That(result.CostUsd, Is.EqualTo(0.00364m));
        });
    }

    [Test]
    public async Task Should_send_the_drafting_model_and_both_prompt_messages()
    {
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return SuccessResponse(ValidTaxonomyContent, 1, 1);
        });
        var client = new OpenAiFamilyDraftClient(new HttpClient(handler), Options(), DraftOptions(), CreateSender(), NullLogger<OpenAiFamilyDraftClient>.Instance);

        await client.DraftTaxonomy(Prompt(), CancellationToken.None);

        var body = JsonNode.Parse(capturedBody!)!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(body["model"]!.GetValue<string>(), Is.EqualTo("gpt-6-sol"));
            Assert.That(body["messages"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(body["messages"]![0]!["role"]!.GetValue<string>(), Is.EqualTo("system"));
            Assert.That(body["messages"]![1]!["content"]!.GetValue<string>(), Does.Contain("ps5 controller"));
        });
    }

    [Test]
    public void Should_throw_when_the_drafted_taxonomy_does_not_gate_exactly_one_in_scope_choice()
    {
        var handler = new StubHandler(_ => SuccessResponse(UngatedTaxonomyContent, 1, 1));
        var client = new OpenAiFamilyDraftClient(new HttpClient(handler), Options(), DraftOptions(), CreateSender(), NullLogger<OpenAiFamilyDraftClient>.Instance);

        Assert.ThrowsAsync<TaxonomyParseException>(
            () => client.DraftTaxonomy(Prompt(), CancellationToken.None));
    }

    private static HttpResponseMessage SuccessResponse(string content, int promptTokens, int completionTokens)
    {
        var envelope = new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["content"] = content } } },
            ["usage"] = new JsonObject { ["prompt_tokens"] = promptTokens, ["completion_tokens"] = completionTokens }
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = request => Task.FromResult(respond(request));
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _respond(request);
    }
}
