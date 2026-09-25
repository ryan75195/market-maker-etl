using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchPayloadSegmentationFieldsTests
{
    private static readonly string ElectronicsPayload = ReadFixture("search-payload.json");
    private static readonly string ClothingPayload = ReadFixture("search-payload-clothing.json");
    private static readonly string CustomFacetsPayload = ReadFixture("search-payload-custom-facets.json");

    [Test]
    public void Should_read_category_hierarchy_brand_condition_and_seller_ids_from_a_search_payload_item()
    {
        var summary = new MercariSearchParser().Parse(ElectronicsPayload).Listings[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.CategoryId, Is.EqualTo(797));
            Assert.That(summary.CategoryHierarchy!.Level0Id, Is.EqualTo(7));
            Assert.That(summary.CategoryHierarchy.Level0Name, Is.EqualTo("Electronics"));
            Assert.That(summary.CategoryHierarchy.Level1Id, Is.EqualTo(84));
            Assert.That(summary.CategoryHierarchy.Level1Name, Is.EqualTo("Video games & consoles"));
            Assert.That(summary.CategoryHierarchy.Level2Id, Is.EqualTo(797));
            Assert.That(summary.CategoryHierarchy.Level2Name, Is.EqualTo("Consoles"));
            Assert.That(summary.BrandId, Is.EqualTo(5058));
            Assert.That(summary.ConditionId, Is.EqualTo(3));
            Assert.That(summary.SellerId, Is.EqualTo(865070813L));
            Assert.That(summary.RawJson, Does.Contain("m71344610988"));
        });
    }

    [Test]
    public void Should_leave_shipping_payer_and_color_null_because_the_search_payload_never_reports_them()
    {
        var summary = new MercariSearchParser().Parse(ElectronicsPayload).Listings[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.ShippingPayer, Is.Null);
            Assert.That(summary.ColorName, Is.Null);
        });
    }

    [Test]
    public void Should_read_the_size_name_from_a_clothing_search_payload_item()
    {
        var summary = new MercariSearchParser().Parse(ClothingPayload).Listings[0];

        Assert.That(summary.SizeName, Is.EqualTo("4.5"));
    }

    [Test]
    public void Should_read_custom_facets_into_attributes()
    {
        var summary = new MercariSearchParser().Parse(CustomFacetsPayload).Listings[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.Attributes, Is.Not.Null);
            Assert.That(summary.Attributes!["Model"], Is.EqualTo("PULSE 3D Wireless Gaming Headset"));
            Assert.That(summary.Attributes["Platform"], Is.EqualTo("PlayStation 5"));
        });
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
