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

    private const string SoldOutItemPayload = """
        {"data":{"search":{"itemsList":[{"id":"m1","name":"PS5 Controller","status":"sold_out","price":5000}]}}}
        """;

    private static readonly string CapturedPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-payload.json"));

    private static readonly string CapturedActiveFilteredPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-payload-active-filtered.json"));

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
    public void Should_leave_sold_date_absent_when_parsing_a_captured_in_transaction_item()
    {
        var summary = new MercariSearchParser().Parse(CapturedPayload).Listings[2];

        Assert.That(summary.SoldDate, Is.Null);
    }

    [Test]
    public void Should_mark_a_payload_item_that_is_sold_out_as_sold()
    {
        var summaries = new MercariSearchParser().Parse(SoldOutItemPayload).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].IsSold, Is.True);
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

    [Test]
    public void Should_report_listings_present_when_a_payload_yields_no_identifiable_items()
    {
        var parser = new MercariSearchParser();

        Assert.Multiple(() =>
        {
            Assert.That(parser.Parse(ItemsWithoutIdentifiersPayload).Listings, Is.Empty);
            Assert.That(parser.ContainsListingMarkup(ItemsWithoutIdentifiersPayload), Is.True);
        });
    }

    [Test]
    public void Should_throw_for_a_payload_without_a_search_object()
    {
        var parser = new MercariSearchParser();

        var exception = Assert.Throws<UnrecognisedSearchPageException>(() => parser.Parse(ErrorPayload));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("Unrecognised search page"));
            Assert.That(parser.ContainsListingMarkup(ErrorPayload), Is.True);
        });
    }

    [Test]
    public void Should_report_listings_present_for_a_captured_payload()
    {
        Assert.That(new MercariSearchParser().ContainsListingMarkup(CapturedPayload), Is.True);
    }

    [Test]
    public void Should_contain_only_on_sale_items_in_a_captured_active_filtered_response()
    {
        var summaries = new MercariSearchParser().Parse(CapturedActiveFilteredPayload).Listings;

        Assert.That(summaries.Select(summary => summary.IsSold), Is.All.False);
    }

    [Test]
    public void Should_expose_the_reduced_reported_total_from_a_captured_active_filtered_response()
    {
        var result = new MercariSearchParser().Parse(CapturedActiveFilteredPayload);

        Assert.That(result.TotalCount, Is.EqualTo(8095));
    }
}
