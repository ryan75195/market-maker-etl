using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiSchemaBuilder
{
    private const string IdProperty = "id";
    private const string AmbiguousProperty = "ambiguous";
    private const string LabelsProperty = "labels";

    public static JsonObject Build(IReadOnlyDictionary<string, ClassifyQuestion> questions)
    {
        var itemSchema = BuildItemSchema(questions);
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(JsonValue.Create(LabelsProperty)),
            ["properties"] = new JsonObject
            {
                [LabelsProperty] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = itemSchema
                }
            }
        };
    }

    private static JsonObject BuildItemSchema(IReadOnlyDictionary<string, ClassifyQuestion> questions)
    {
        var properties = new JsonObject
        {
            [IdProperty] = StringSchema(),
            [AmbiguousProperty] = StringSchema()
        };
        var required = new JsonArray(JsonValue.Create(IdProperty), JsonValue.Create(AmbiguousProperty));

        foreach (var (key, question) in questions)
        {
            properties[key] = EnumSchema(question.Criteria.Keys);
            required.Add(JsonValue.Create(key));
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties
        };
    }

    private static JsonObject StringSchema() => new() { ["type"] = "string" };

    private static JsonObject EnumSchema(IEnumerable<string> options)
    {
        var array = new JsonArray();
        foreach (var option in options)
        {
            array.Add(JsonValue.Create(option));
        }

        return new JsonObject { ["type"] = "string", ["enum"] = array };
    }
}
