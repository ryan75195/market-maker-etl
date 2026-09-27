using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetchFailedExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new FetchFailedException("The fetcher sidecar returned 500.");

        Assert.That(exception.Message, Is.EqualTo("The fetcher sidecar returned 500."));
    }

    [Test]
    public void Should_expose_the_inner_exception_it_was_constructed_with()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new FetchFailedException("failed", inner);

        Assert.That(exception.InnerException, Is.SameAs(inner));
    }
}
