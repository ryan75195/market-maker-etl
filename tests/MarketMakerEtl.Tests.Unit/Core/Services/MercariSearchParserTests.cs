using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchParserTests
{
    private const string ResultsPage = """
        <div data-testid="ItemContainer" data-productid="m92390261760" data-itemprice="1900" data-itemstatus="on_sale" data-brand="PlayStation">
          <a href="https://www.mercari.com/us/item/m92390261760/">
            <img src="https://static.mercdn.net/m92390261760.jpg" alt="PlayStation 5 Console" />
          </a>
          <span data-testid="ItemName">PlayStation 5 Console</span>
        </div>
        <div data-testid="ItemContainer" data-productid="m92390261761" data-itemprice="2500" data-itemstatus="trading" data-brand="Nintendo">
          <a href="https://www.mercari.com/us/item/m92390261761/">
            <img src="https://static.mercdn.net/m92390261761.jpg" alt="Nintendo Switch OLED" />
          </a>
          <span data-testid="ItemName">Nintendo Switch OLED</span>
        </div>
        """;

    private const string CardWithoutPrice = """
        <div data-testid="ItemContainer" data-productid="m92390261762" data-itemstatus="on_sale" data-brand="Sony">
          <a href="https://www.mercari.com/us/item/m92390261762/">
            <img src="https://static.mercdn.net/m92390261762.jpg" alt="Sony DualSense" />
          </a>
          <span data-testid="ItemName">Sony DualSense</span>
        </div>
        """;

    private const string SoldOutCard = """
        <div data-testid="ItemContainer" data-productid="m92390261763" data-itemprice="5000" data-itemstatus="sold_out" data-brand="Sony">
          <a href="https://www.mercari.com/us/item/m92390261763/">
            <img src="https://static.mercdn.net/m92390261763.jpg" alt="Sony DualSense" />
          </a>
          <span data-testid="ItemName">Sony DualSense</span>
        </div>
        """;

    private static readonly string CapturedRenderedCards = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-rendered-cards.html"));

    [Test]
    public void Should_read_price_and_sold_state_from_the_tile_wrapping_a_captured_card()
    {
        var summaries = new MercariSearchParser().Parse(CapturedRenderedCards).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(2));
            Assert.That(summaries[0].ListingId, Is.EqualTo("m71344610988"));
            Assert.That(summaries[0].Title, Is.EqualTo("Sony PlayStation 5 PS5 Digital Console with Controller and Power Cable"));
            Assert.That(summaries[0].Price, Is.EqualTo(389.00m));
            Assert.That(summaries[0].Url, Is.EqualTo("https://www.mercari.com/us/item/m71344610988/"));
            Assert.That(summaries[0].Brand, Is.EqualTo("PlayStation"));
            Assert.That(summaries[0].IsSold, Is.False);
            Assert.That(summaries[1].ListingId, Is.EqualTo("m44688360101"));
            Assert.That(summaries[1].Price, Is.EqualTo(140.25m));
            Assert.That(summaries[1].IsSold, Is.True);
        });
    }

    [Test]
    public void Should_parse_one_listing_per_result_card()
    {
        var summaries = new MercariSearchParser().Parse(ResultsPage).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(2));
            Assert.That(summaries[0].ListingId, Is.EqualTo("m92390261760"));
            Assert.That(summaries[0].Title, Is.EqualTo("PlayStation 5 Console"));
            Assert.That(summaries[0].Price, Is.EqualTo(19.00m));
            Assert.That(summaries[0].Currency, Is.EqualTo("USD"));
            Assert.That(summaries[0].Url, Is.EqualTo("https://www.mercari.com/us/item/m92390261760/"));
            Assert.That(summaries[0].PrimaryImageUrl, Is.EqualTo("https://static.mercdn.net/m92390261760.jpg"));
            Assert.That(summaries[0].Brand, Is.EqualTo("PlayStation"));
            Assert.That(summaries[0].IsSold, Is.False);
        });
    }

    [Test]
    public void Should_preserve_the_full_listing_id_including_its_leading_letter()
    {
        var summaries = new MercariSearchParser().Parse(ResultsPage).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries[0].ListingId, Is.EqualTo("m92390261760"));
            Assert.That(summaries[1].ListingId, Is.EqualTo("m92390261761"));
        });
    }

    [Test]
    public void Should_mark_a_card_that_is_in_transaction_as_sold()
    {
        var summaries = new MercariSearchParser().Parse(ResultsPage).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries[1].IsSold, Is.True);
            Assert.That(summaries[1].Brand, Is.EqualTo("Nintendo"));
        });
    }

    [Test]
    public void Should_mark_a_card_that_is_sold_out_as_sold()
    {
        var summaries = new MercariSearchParser().Parse(SoldOutCard).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].IsSold, Is.True);
        });
    }

    [Test]
    public void Should_leave_price_absent_when_a_card_has_no_price()
    {
        var summaries = new MercariSearchParser().Parse(CardWithoutPrice).Listings;

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].Price, Is.Null);
            Assert.That(summaries[0].ListingId, Is.EqualTo("m92390261762"));
        });
    }

    [Test]
    public void Should_report_listing_markup_only_when_the_page_contains_result_cards()
    {
        var parser = new MercariSearchParser();

        Assert.Multiple(() =>
        {
            Assert.That(parser.ContainsListingMarkup(ResultsPage), Is.True);
            Assert.That(parser.ContainsListingMarkup("<html><body>No results found</body></html>"), Is.False);
        });
    }

    [Test]
    public void Should_throw_with_the_challenge_message_for_a_cloudflare_challenge_page()
    {
        const string challengePage = """
            <html>
              <head><title>Just a moment...</title></head>
              <body>
                <script src="/cdn-cgi/challenge-platform/h/g/orchestrate/jsch/v1"></script>
              </body>
            </html>
            """;

        var exception = Assert.Throws<UnrecognisedSearchPageException>(
            () => new MercariSearchParser().Parse(challengePage));

        Assert.That(exception!.Message, Is.EqualTo("Cloudflare challenge page"));
    }

    [Test]
    public void Should_throw_with_the_page_title_for_an_unrecognised_html_page()
    {
        const string page = "<html><head><title>Access Denied</title></head><body>blocked</body></html>";

        var exception = Assert.Throws<UnrecognisedSearchPageException>(
            () => new MercariSearchParser().Parse(page));

        Assert.That(exception!.Message, Is.EqualTo("Unrecognised search page (title: 'Access Denied')"));
    }
}
