using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchPayloadParsesToListingsTests
{
    private const string EmptyResultPayload = """
        {"data":{"search":{"count":0,"itemsList":[],"__typename":"SearchResponse"}}}
        """;

    private const string ItemsWithoutIdentifiersPayload = """
        {"data":{"search":{"itemsList":[{"name":"PS5","status":"on_sale","price":1000}]}}}
        """;

    private const string ErrorPayload = """
        {"errors":[{"message":"Invalid request"}]}
        """;

    private static readonly string CapturedPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-payload.json"));

    [Test]
    public void Should_parse_every_item_of_a_captured_search_payload()
    {
        var summaries = new MercariSearchParser().Parse(CapturedPayload).Listings;

        Assert.That(
            summaries.Select(summary => summary.ListingId),
            Is.EqualTo(new[] { "m71344610988", "m89945662386", "m44688360101" }));
    }

    [Test]
    public void Should_expose_the_reported_total_count_from_the_payload()
    {
        var result = new MercariSearchParser().Parse(CapturedPayload);

        Assert.That(result.TotalCount, Is.EqualTo(20783));
    }

    [Test]
    public void Should_read_listing_attributes_from_a_payload_item()
    {
        var summary = new MercariSearchParser().Parse(CapturedPayload).Listings[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.Title, Is.EqualTo("Sony PlayStation 5 PS5 Digital Console with Controller and Power Cable"));
            Assert.That(summary.Price, Is.EqualTo(389.00m));
            Assert.That(summary.Currency, Is.EqualTo("USD"));
            Assert.That(summary.Url, Is.EqualTo("https://www.mercari.com/us/item/m71344610988/"));
            Assert.That(summary.PrimaryImageUrl, Is.EqualTo("https://u-mercari-images.mercdn.net/photos/m71344610988_1.jpg?1790200178"));
            Assert.That(summary.Brand, Is.EqualTo("PlayStation"));
            Assert.That(summary.Condition, Is.EqualTo("Good"));
            Assert.That(summary.BuyingFormat, Is.Null);
            Assert.That(summary.IsSold, Is.False);
        });
    }

    [Test]
    public void Should_mark_a_payload_item_in_transaction_as_sold()
    {
        var summaries = new MercariSearchParser().Parse(CapturedPayload).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries[2].IsSold, Is.True);
            Assert.That(summaries[2].Price, Is.EqualTo(140.25m));
            Assert.That(summaries[1].IsSold, Is.False);
        });
    }

    [Test]
    public void Should_treat_an_empty_payload_as_a_genuinely_empty_search()
    {
        var parser = new MercariSearchParser();

        Assert.Multiple(() =>
        {
            Assert.That(parser.Parse(EmptyResultPayload).Listings, Is.Empty);
            Assert.That(parser.ContainsListingMarkup(EmptyResultPayload), Is.False);
        });
    }

    [TestCase(ItemsWithoutIdentifiersPayload)]
    [TestCase(ErrorPayload)]
    public void Should_report_listings_present_when_a_payload_yields_no_readable_items(string payload)
    {
        var parser = new MercariSearchParser();

        Assert.Multiple(() =>
        {
            Assert.That(parser.Parse(payload).Listings, Is.Empty);
            Assert.That(parser.ContainsListingMarkup(payload), Is.True);
        });
    }

    [Test]
    public void Should_report_listings_present_for_a_captured_payload()
    {
        Assert.That(new MercariSearchParser().ContainsListingMarkup(CapturedPayload), Is.True);
    }
}
