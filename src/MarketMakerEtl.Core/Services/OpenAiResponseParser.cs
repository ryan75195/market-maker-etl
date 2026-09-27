using System.Text.Json;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiResponseParser
{
    private const string LabelsProperty = "labels";
    private const string IdProperty = "id";
    private const string AmbiguousProperty = "ambiguous";
    private const double LoweredConfidenceMargin = 0.1;

    public static IReadOnlyDictionary<string, ClassifyResult> Parse(
        string content, IReadOnlyDictionary<string, ClassifyQuestion> questions, double reviewThreshold)
    {
        try
        {
            return ParseLabels(content, questions, reviewThreshold);
        }
        catch (JsonException ex)
        {
            throw new ListingClassifierException("OpenAI returned a malformed response body.", ex);
        }
    }

    private static IReadOnlyDictionary<string, ClassifyResult> ParseLabels(
        string content, IReadOnlyDictionary<string, ClassifyQuestion> questions, double reviewThreshold)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty(LabelsProperty, out var labels)
            || labels.ValueKind != JsonValueKind.Array)
        {
            throw new ListingClassifierException("OpenAI response did not contain a 'labels' array.");
        }

        var results = new Dictionary<string, ClassifyResult>(StringComparer.Ordinal);
        foreach (var label in labels.EnumerateArray())
        {
            var id = ReadString(label, IdProperty);
            if (id.Length == 0)
            {
                continue;
            }

            results[id] = BuildResult(label, questions, reviewThreshold);
        }

        return results;
    }

    private static ClassifyResult BuildResult(
        JsonElement label, IReadOnlyDictionary<string, ClassifyQuestion> questions, double reviewThreshold)
    {
        var ambiguous = ReadString(label, AmbiguousProperty);
        var mentionedKeys = FindMentionedQuestions(ambiguous, questions.Keys);
        var answers = questions.Keys.ToDictionary(
            key => key,
            key => BuildAnswer(label, key, ambiguous, mentionedKeys, reviewThreshold),
            StringComparer.Ordinal);

        return new ClassifyResult(answers);
    }

    private static ClassifyAnswer BuildAnswer(
        JsonElement label,
        string questionKey,
        string ambiguous,
        IReadOnlyCollection<string> mentionedKeys,
        double reviewThreshold)
    {
        var choice = ReadString(label, questionKey);
        var confidence = ComputeConfidence(ambiguous, questionKey, mentionedKeys, reviewThreshold);
        return new ClassifyAnswer(choice, confidence, 1.0, new Dictionary<string, double>());
    }

    private static double ComputeConfidence(
        string ambiguous, string questionKey, IReadOnlyCollection<string> mentionedKeys, double reviewThreshold)
    {
        if (ambiguous.Length == 0)
        {
            return 1.0;
        }

        var appliesToAllQuestions = mentionedKeys.Count == 0;
        return appliesToAllQuestions || mentionedKeys.Contains(questionKey)
            ? Math.Clamp(reviewThreshold - LoweredConfidenceMargin, 0.0, 1.0)
            : 1.0;
    }

    private static IReadOnlyCollection<string> FindMentionedQuestions(string ambiguous, IEnumerable<string> questionKeys) =>
        ambiguous.Length == 0
            ? []
            : questionKeys.Where(key => ambiguous.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
