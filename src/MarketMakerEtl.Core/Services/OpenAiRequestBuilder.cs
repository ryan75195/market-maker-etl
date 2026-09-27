using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiRequestBuilder
{
    private const string SystemRole = "system";
    private const string UserRole = "user";
    private const string UserPreamble = "Label these listings. Return {\"labels\": [...]} in the same order.\n";

    public static JsonObject Build(
        string model,
        string reasoningEffort,
        string systemPrompt,
        IReadOnlyDictionary<string, ClassifyQuestion> questions,
        IReadOnlyList<ClassifyListingState> batch)
    {
        var schema = OpenAiSchemaBuilder.Build(questions);
        return new JsonObject
        {
            ["model"] = model,
            ["reasoning_effort"] = reasoningEffort,
            ["messages"] = new JsonArray
            {
                BuildMessage(SystemRole, systemPrompt),
                BuildMessage(UserRole, BuildUserContent(batch))
            },
            ["response_format"] = BuildResponseFormat(schema)
        };
    }

    private static JsonObject BuildResponseFormat(JsonObject schema) =>
        new()
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "labels",
                ["strict"] = true,
                ["schema"] = schema
            }
        };

    private static JsonObject BuildMessage(string role, string content) =>
        new() { ["role"] = role, ["content"] = content };

    private static string BuildUserContent(IReadOnlyList<ClassifyListingState> batch)
    {
        var listings = new JsonArray();
        foreach (var state in batch)
        {
            listings.Add(BuildListing(state));
        }

        return UserPreamble + listings.ToJsonString();
    }

    private static JsonObject BuildListing(ClassifyListingState state) =>
        new()
        {
            ["id"] = state.Id,
            ["title"] = state.Title,
            ["description"] = state.Description,
            ["category"] = state.Category,
            ["brand"] = state.Brand,
            ["sold"] = state.Sold
        };
}
