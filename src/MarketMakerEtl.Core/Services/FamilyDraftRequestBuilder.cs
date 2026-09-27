using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Services;

internal static class FamilyDraftRequestBuilder
{
    private const string SystemRole = "system";
    private const string UserRole = "user";

    public static JsonObject Build(string model, string reasoningEffort, FamilyDraftMessages messages) =>
        new()
        {
            ["model"] = model,
            ["reasoning_effort"] = reasoningEffort,
            ["messages"] = new JsonArray
            {
                BuildMessage(SystemRole, messages.SystemPrompt),
                BuildMessage(UserRole, messages.UserPrompt)
            },
            ["response_format"] = new JsonObject { ["type"] = "json_object" }
        };

    private static JsonObject BuildMessage(string role, string content) =>
        new() { ["role"] = role, ["content"] = content };
}
