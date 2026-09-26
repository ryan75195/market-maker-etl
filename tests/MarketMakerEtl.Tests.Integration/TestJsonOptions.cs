using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketMakerEtl.Tests.Integration;

public static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
