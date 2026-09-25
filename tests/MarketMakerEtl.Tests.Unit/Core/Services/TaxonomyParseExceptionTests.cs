using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TaxonomyParseExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new TaxonomyParseException("questions must be a JSON object.");

        Assert.That(exception.Message, Is.EqualTo("questions must be a JSON object."));
    }

    [Test]
    public void Should_be_thrown_by_the_parser_for_a_structurally_malformed_document()
    {
        Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse("not json"));
    }
}
