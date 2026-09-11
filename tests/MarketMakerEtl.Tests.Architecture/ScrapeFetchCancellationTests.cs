using FluentAssertions;
using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Tests.Architecture;

[TestFixture]
public class ScrapeFetchCancellationTests
{
    private static readonly Type[] ScrapeFetchInterfaces =
    [
        typeof(IScrapeClient),
        typeof(IScrapeContentStore),
        typeof(ISearchPageService)
    ];

    [Test]
    public void Should_declare_a_cancellation_token_on_every_scrape_fetch_interface_method()
    {
        var violations = new List<string>();

        foreach (var fetchInterface in ScrapeFetchInterfaces)
        {
            foreach (var method in fetchInterface.GetMethods())
            {
                var declaresCancellationToken = method.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(CancellationToken));

                if (!declaresCancellationToken)
                {
                    violations.Add($"  {fetchInterface.Name}.{method.Name}");
                }
            }
        }

        violations.Should().BeEmpty(
            "Every scrape-fetch interface method must accept a CancellationToken. " +
            $"Violations ({violations.Count}):\n{string.Join("\n", violations)}");
    }
}
