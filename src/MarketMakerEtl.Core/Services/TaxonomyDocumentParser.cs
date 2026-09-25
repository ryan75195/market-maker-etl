using System.Text.Json;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class TaxonomyDocumentParser
{
    public static TaxonomyDocument Parse(string questionsJson)
    {
        using var document = ParseDocument(questionsJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TaxonomyParseException("Taxonomy document must be a JSON object.");
        }

        return new TaxonomyDocument(ReadFamily(root), ReadVersion(root), ReadQuestions(root));
    }

    private static JsonDocument ParseDocument(string questionsJson)
    {
        try
        {
            return JsonDocument.Parse(questionsJson);
        }
        catch (JsonException ex)
        {
            throw new TaxonomyParseException($"Taxonomy document is not valid JSON: {ex.Message}", ex);
        }
    }

    private static string ReadFamily(JsonElement root)
    {
        if (!root.TryGetProperty("family", out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new TaxonomyParseException("family must be a string.");
        }

        return value.GetString() ?? string.Empty;
    }

    private static int ReadVersion(JsonElement root) =>
        root.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static IReadOnlyList<TaxonomyQuestion> ReadQuestions(JsonElement root)
    {
        if (!root.TryGetProperty("questions", out var questionsElement))
        {
            throw new TaxonomyParseException("questions is required.");
        }

        if (questionsElement.ValueKind != JsonValueKind.Object)
        {
            throw new TaxonomyParseException("questions must be a JSON object.");
        }

        return questionsElement.EnumerateObject()
            .Select(property => ReadQuestion(property.Name, property.Value))
            .ToList();
    }

    private static TaxonomyQuestion ReadQuestion(string key, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new TaxonomyParseException($"Question '{key}' must be a JSON object.");
        }

        return new(
            key,
            ReadInstructions(key, element),
            ReadCriteria(key, element),
            ReadAskWhen(key, element),
            ReadNotStatedMeans(key, element));
    }

    private static string ReadInstructions(string questionKey, JsonElement element)
    {
        if (!element.TryGetProperty("instructions", out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' instructions must be a string.");
        }

        return value.GetString() ?? string.Empty;
    }

    private static IReadOnlyDictionary<string, string> ReadCriteria(string questionKey, JsonElement element)
    {
        if (!element.TryGetProperty("criteria", out var criteriaElement))
        {
            return new Dictionary<string, string>();
        }

        if (criteriaElement.ValueKind != JsonValueKind.Object)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' criteria must be a JSON object of strings.");
        }

        var criteria = new Dictionary<string, string>();
        foreach (var property in criteriaElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new TaxonomyParseException(
                    $"Question '{questionKey}' criteria value '{property.Name}' must be a string.");
            }

            criteria[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return criteria;
    }

    private static IReadOnlyList<TaxonomyAskWhenClause> ReadAskWhen(string questionKey, JsonElement element)
    {
        if (!element.TryGetProperty("askWhen", out var askWhenElement))
        {
            return [];
        }

        if (askWhenElement.ValueKind != JsonValueKind.Array)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' askWhen must be an array.");
        }

        return askWhenElement.EnumerateArray()
            .Select(clause => ReadAskWhenClause(questionKey, clause))
            .ToList();
    }

    private static TaxonomyAskWhenClause ReadAskWhenClause(string questionKey, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' askWhen entries must be objects.");
        }

        if (!element.TryGetProperty("question", out var questionValue)
            || questionValue.ValueKind != JsonValueKind.String)
        {
            throw new TaxonomyParseException(
                $"Question '{questionKey}' askWhen entries must have a string 'question'.");
        }

        return new TaxonomyAskWhenClause(questionValue.GetString() ?? string.Empty, ReadAnyOf(questionKey, element));
    }

    private static IReadOnlyList<string> ReadAnyOf(string questionKey, JsonElement element)
    {
        if (!element.TryGetProperty("anyOf", out var anyOfElement) || anyOfElement.ValueKind != JsonValueKind.Array)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' askWhen entries must have an array 'anyOf'.");
        }

        var values = new List<string>();
        foreach (var value in anyOfElement.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new TaxonomyParseException($"Question '{questionKey}' askWhen anyOf entries must be strings.");
            }

            values.Add(value.GetString() ?? string.Empty);
        }

        return values;
    }

    private static string? ReadNotStatedMeans(string questionKey, JsonElement element)
    {
        if (!element.TryGetProperty("notStatedMeans", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new TaxonomyParseException($"Question '{questionKey}' notStatedMeans must be a string.");
        }

        return value.GetString();
    }
}
