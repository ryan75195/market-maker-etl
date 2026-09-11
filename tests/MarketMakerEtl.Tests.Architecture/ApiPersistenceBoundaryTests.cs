using FluentAssertions;
using NetArchTest.Rules;

namespace MarketMakerEtl.Tests.Architecture;

[TestFixture]
public class ApiPersistenceBoundaryTests
{
    private static readonly string[] PersistenceTypeNames =
    [
        "MarketMakerEtl.Core.Data.Entities",
        "MarketMakerEtl.Core.Data.ScrapeStore"
    ];

    [Test]
    public void Should_not_reference_persistence_store_or_entity_types_from_the_api_assembly()
    {
        var result = Types.InAssembly(TestHelpers.ApiAssembly)
            .ShouldNot().HaveDependencyOnAny(PersistenceTypeNames)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "The API must serve read models through Core interfaces rather than persistence types. " +
            $"Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
