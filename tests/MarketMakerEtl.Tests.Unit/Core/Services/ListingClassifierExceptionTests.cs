using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingClassifierExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new ListingClassifierException("classifier returned 500.");

        Assert.That(exception.Message, Is.EqualTo("classifier returned 500."));
    }

    [Test]
    public void Should_expose_the_inner_exception_it_was_constructed_with()
    {
        var inner = new InvalidOperationException("timed out");

        var exception = new ListingClassifierException("classifier timed out.", inner);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("classifier timed out."));
            Assert.That(exception.InnerException, Is.SameAs(inner));
        });
    }

    [Test]
    public void Should_use_the_default_exception_message_for_the_parameterless_constructor()
    {
        var exception = new ListingClassifierException();

        Assert.That(exception.Message, Does.Contain(nameof(ListingClassifierException)));
    }
}
