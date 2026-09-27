using System.Text.Json;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariItemPageMapsAdditionalAttributesTests
{
    private const string ItemWithAttributes = """
        {
          "additionalAttributes": [
            { "__typename": "ItemDetailAdditionalAttribute", "text": "Size: Large" },
            { "__typename": "ItemDetailAdditionalAttribute", "text": "Material: Cotton" }
          ]
        }
        """;

    private const string ItemWithEmptyAttributes = """{ "additionalAttributes": [] }""";

    private const string ItemWithoutAttributesField = "{}";

    [Test]
    public void Should_map_each_attributes_text_field_into_a_positional_key_because_the_schema_has_no_name_or_key_field()
    {
        using var document = JsonDocument.Parse(ItemWithAttributes);

        var attributes = MercariItemDetailSegmentationReader.ReadAdditionalAttributes(document.RootElement);

        Assert.That(attributes, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(attributes!["Attribute0"], Is.EqualTo("Size: Large"));
            Assert.That(attributes["Attribute1"], Is.EqualTo("Material: Cotton"));
        });
    }

    [Test]
    public void Should_return_null_when_the_additional_attributes_array_is_empty()
    {
        using var document = JsonDocument.Parse(ItemWithEmptyAttributes);

        var attributes = MercariItemDetailSegmentationReader.ReadAdditionalAttributes(document.RootElement);

        Assert.That(attributes, Is.Null);
    }

    [Test]
    public void Should_return_null_when_the_item_has_no_additional_attributes_field()
    {
        using var document = JsonDocument.Parse(ItemWithoutAttributesField);

        var attributes = MercariItemDetailSegmentationReader.ReadAdditionalAttributes(document.RootElement);

        Assert.That(attributes, Is.Null);
    }
}
