using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class NonListingPageIsNotReportedAsAListingTests
{
    private const string AntiBotPage = """
        <html>
          <body>
            <h1>Pardon Our Interruption</h1>
            <p>As you were browsing, something about your browser made us think you were a bot.</p>
          </body>
        </html>
        """;

    private const string CatalogPage = """
        <html>
          <body>
            <ul class=ebay-catalog>
              <li><a href=https://www.ebay.co.uk/b/Headphones/112529>Headphones</a></li>
              <li><a href=https://www.ebay.co.uk/b/Cameras/625>Cameras</a></li>
            </ul>
          </body>
        </html>
        """;

    [Test]
    public void Should_report_an_anti_bot_page_as_not_a_listing()
    {
        var listing = EbayItemPageParser.Parse(AntiBotPage);

        Assert.That(listing, Is.Null);
    }

    [Test]
    public void Should_report_a_catalog_page_as_not_a_listing()
    {
        var listing = EbayItemPageParser.Parse(CatalogPage);

        Assert.That(listing, Is.Null);
    }
}
