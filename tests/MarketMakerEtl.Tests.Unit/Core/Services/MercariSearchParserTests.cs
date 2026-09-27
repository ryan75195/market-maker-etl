using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchParserTests
{
    private static readonly string SearchPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-api-payload-nintendo-switch.json"));

    private const string ColorPayload = """
        {
          "data": {
            "search": {
              "count": 1,
              "itemsList": [
                {
                  "id": "m11111111111",
                  "name": "Blue hoodie",
                  "price": 2000,
                  "status": "on_sale",
                  "originalPrice": 2000,
                  "categoryId": 1,
                  "color": { "id": 8, "name": "Blue", "hexCode": "#0047BB" },
                  "photos": [],
                  "itemCondition": { "id": 1, "name": "New" },
                  "brand": null,
                  "itemSize": { "name": "M" },
                  "itemCategory": { "name": "Tops" },
                  "itemCategoryHierarchy": [],
                  "seller": { "sellerId": 42 },
                  "shippingPayer": { "code": "seller" },
                  "customFacetsList": []
                }
              ]
            }
          }
        }
        """;

    [Test]
    public void Should_parse_every_listing_from_a_captured_ad_hoc_search_payload()
    {
        var summaries = new MercariSearchParser().Parse(SearchPayload).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Is.Not.Empty);
            Assert.That(summaries[0].ListingId, Is.EqualTo("m45718142917"));
            Assert.That(summaries[0].Title, Is.EqualTo("Nintendo switch v1 bundle"));
            Assert.That(summaries[0].Price, Is.EqualTo(150.00m));
            Assert.That(summaries[0].Url, Is.EqualTo("https://www.mercari.com/us/item/m45718142917/"));
            Assert.That(summaries[0].IsSold, Is.False);
            Assert.That(summaries[0].Condition, Is.EqualTo("Good"));
            Assert.That(summaries[0].Brand, Is.EqualTo("Nintendo"));
            Assert.That(summaries[0].SellerId, Is.EqualTo(734240498L));
        });
    }

    [Test]
    public void Should_report_the_total_result_count_from_the_search_payload()
    {
        var result = new MercariSearchParser().Parse(SearchPayload);

        Assert.That(result.TotalCount, Is.EqualTo(10000));
    }

    [Test]
    public void Should_read_the_color_name_from_the_nested_color_object_because_color_is_an_object_not_a_string()
    {
        var summary = new MercariSearchParser().Parse(ColorPayload).Listings[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.ColorName, Is.EqualTo("Blue"));
            Assert.That(summary.ShippingPayer, Is.EqualTo("seller"));
            Assert.That(summary.SizeName, Is.EqualTo("M"));
            Assert.That(summary.SellerId, Is.EqualTo(42L));
        });
    }

    [Test]
    public void Should_report_listing_markup_only_when_the_payload_has_a_non_empty_result_set()
    {
        var parser = new MercariSearchParser();

        Assert.Multiple(() =>
        {
            Assert.That(parser.ContainsListingMarkup(SearchPayload), Is.True);
            Assert.That(
                parser.ContainsListingMarkup("""{"data":{"search":{"count":0,"itemsList":[]}}}"""),
                Is.False);
        });
    }

    [Test]
    public void Should_throw_for_a_payload_without_a_search_result()
    {
        var exception = Assert.Throws<UnrecognisedSearchPageException>(
            () => new MercariSearchParser().Parse("""{"data":{"somethingElse":true}}"""));

        Assert.That(exception!.Message, Is.EqualTo("Unrecognised search payload"));
    }

    [Test]
    public void Should_throw_for_content_that_is_not_a_json_payload()
    {
        var exception = Assert.Throws<UnrecognisedSearchPageException>(
            () => new MercariSearchParser().Parse("<html><head><title>Just a moment...</title></head></html>"));

        Assert.That(exception!.Message, Is.EqualTo("Unrecognised search payload"));
    }
}
