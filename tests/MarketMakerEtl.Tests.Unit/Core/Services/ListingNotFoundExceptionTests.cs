using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ListingNotFoundExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new ListingNotFoundException("m1 was not found by the fetcher sidecar.");

        Assert.That(exception.Message, Is.EqualTo("m1 was not found by the fetcher sidecar."));
    }

    [Test]
    public void Should_expose_the_inner_exception_it_was_constructed_with()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new ListingNotFoundException("not found", inner);

        Assert.That(exception.InnerException, Is.SameAs(inner));
    }
}
