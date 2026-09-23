using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

public partial class MercariItemPageParserTests
{
    private const string PageWithNoItemRecordOrHooks = """
        <html>
          <body>
            <h1>Pardon Our Interruption</h1>
            <p>As you were browsing, something about your browser made us think you were a bot.</p>
          </body>
        </html>
        """;

    [Test]
    public void Should_return_null_for_a_page_with_no_item_record_and_no_hooks()
    {
        var listing = new MercariItemPageParser().Parse(PageWithNoItemRecordOrHooks);

        Assert.That(listing, Is.Null);
    }
}
