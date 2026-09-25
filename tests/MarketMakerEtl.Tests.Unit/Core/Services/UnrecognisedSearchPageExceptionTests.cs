using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class UnrecognisedSearchPageExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new UnrecognisedSearchPageException("Cloudflare challenge page");

        Assert.That(exception.Message, Is.EqualTo("Cloudflare challenge page"));
    }

    [Test]
    public void Should_be_thrown_by_the_mercari_parser_for_an_unrecognised_page()
    {
        const string page = "<html><head><title>Oops</title></head><body>No cards</body></html>";

        Assert.Throws<UnrecognisedSearchPageException>(() => new MercariSearchParser().Parse(page));
    }
}
