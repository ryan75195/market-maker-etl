using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class FetchInfrastructureUnavailableExceptionTests
{
    [Test]
    public void Should_expose_the_message_it_was_constructed_with()
    {
        var exception = new FetchInfrastructureUnavailableException("Could not reach the fetcher sidecar.");

        Assert.That(exception.Message, Is.EqualTo("Could not reach the fetcher sidecar."));
    }

    [Test]
    public void Should_expose_the_inner_exception_it_was_constructed_with()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new FetchInfrastructureUnavailableException("unavailable", inner);

        Assert.That(exception.InnerException, Is.SameAs(inner));
    }
}
