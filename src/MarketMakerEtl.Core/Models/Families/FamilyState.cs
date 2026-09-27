using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Families;

[JsonConverter(typeof(JsonStringEnumConverter<FamilyState>))]
public enum FamilyState
{
    Draft = 0,
    Active = 1
}
