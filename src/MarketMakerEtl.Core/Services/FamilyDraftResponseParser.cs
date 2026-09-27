using System.Text.Json;
using MarketMakerEtl.Core.Models.Onboarding;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

internal static class FamilyDraftResponseParser
{
    private const string ItemTypeQuestionKey = "item_type";

    public static FamilyDraftParsedTaxonomy Parse(string content)
    {
        var json = ExtractJsonObject(content);
        var document = TaxonomyDocumentParser.Parse(json);
        var validation = TaxonomyValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new TaxonomyParseException(string.Join(" ", validation.Errors));
        }

        ValidateItemTypeGate(document);
        return new FamilyDraftParsedTaxonomy(json, ReadDealGroupBy(json));
    }

    private static void ValidateItemTypeGate(TaxonomyDocument document)
    {
        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(document);
        if (!gates.TryGetValue(ItemTypeQuestionKey, out var choices) || choices.Count != 1)
        {
            throw new TaxonomyParseException(
                "The item_type question must have exactly one in-scope choice referenced by the other questions.");
        }
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var withoutOpeningFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return (closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex] : withoutOpeningFence).Trim();
    }

    private static string? ReadDealGroupBy(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("dealGroupBy", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
