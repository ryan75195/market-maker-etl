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

    [Test]
    public void Should_parse_one_listing_per_result_card()
    {
        var summaries = new MercariSearchParser().Parse(ResultsPage);

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
        var summaries = new MercariSearchParser().Parse(ResultsPage);

        Assert.Multiple(() =>
        {
            Assert.That(summaries[0].ListingId, Is.EqualTo("m92390261760"));
            Assert.That(summaries[1].ListingId, Is.EqualTo("m92390261761"));
        });
    }

    [Test]
    public void Should_mark_a_card_that_is_in_transaction_as_sold()
    {
        var summaries = new MercariSearchParser().Parse(ResultsPage);

        Assert.Multiple(() =>
        {
            Assert.That(summaries[1].IsSold, Is.True);
            Assert.That(summaries[1].Brand, Is.EqualTo("Nintendo"));
        });
    }

    [Test]
    public void Should_leave_price_absent_when_a_card_has_no_price()
    {
        var summaries = new MercariSearchParser().Parse(CardWithoutPrice);

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
}
