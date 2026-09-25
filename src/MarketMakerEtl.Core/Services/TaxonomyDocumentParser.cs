using System.Text.Json;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class TaxonomyDocumentParser
{
    public static TaxonomyDocument Parse(string questionsJson)
    {
        using var document = JsonDocument.Parse(questionsJson);
        var root = document.RootElement;
        return new TaxonomyDocument(ReadFamily(root), ReadVersion(root), ReadQuestions(root));
    }

    private static string ReadFamily(JsonElement root) =>
        root.TryGetProperty("family", out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static int ReadVersion(JsonElement root) =>
        root.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static IReadOnlyList<TaxonomyQuestion> ReadQuestions(JsonElement root)
    {
        if (!root.TryGetProperty("questions", out var questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return questionsElement.EnumerateObject()
            .Select(property => ReadQuestion(property.Name, property.Value))
            .ToList();
    }

    private static TaxonomyQuestion ReadQuestion(string key, JsonElement element) =>
        new(key, ReadInstructions(element), ReadCriteria(element), ReadAskWhen(element), ReadNotStatedMeans(element));

    private static string ReadInstructions(JsonElement element) =>
        element.TryGetProperty("instructions", out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static IReadOnlyDictionary<string, string> ReadCriteria(JsonElement element)
    {
        if (!element.TryGetProperty("criteria", out var criteriaElement)
            || criteriaElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return criteriaElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
    }

    private static IReadOnlyList<TaxonomyAskWhenClause> ReadAskWhen(JsonElement element)
    {
        if (!element.TryGetProperty("askWhen", out var askWhenElement)
            || askWhenElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return askWhenElement.EnumerateArray().Select(ReadAskWhenClause).ToList();
    }

    private static TaxonomyAskWhenClause ReadAskWhenClause(JsonElement element)
    {
        var question = element.TryGetProperty("question", out var questionValue)
            ? questionValue.GetString() ?? string.Empty
            : string.Empty;
        var anyOf = ReadAnyOf(element);
        return new TaxonomyAskWhenClause(question, anyOf);
    }

    private static IReadOnlyList<string> ReadAnyOf(JsonElement element)
    {
        if (!element.TryGetProperty("anyOf", out var anyOfElement) || anyOfElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return anyOfElement.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToList();
    }

    private static string? ReadNotStatedMeans(JsonElement element) =>
        element.TryGetProperty("notStatedMeans", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
