using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariConditionNormalizerTests
{
    [TestCase("New", "new")]
    [TestCase("Like new", "like_new")]
    [TestCase("Good", "good")]
    [TestCase("Fair", "fair")]
    [TestCase("Poor", "poor")]
    [TestCase("good", "good")]
    [TestCase("  Fair  ", "fair")]
    public void Should_normalize_a_known_condition_label_to_its_option(string condition, string expected)
    {
        Assert.That(MercariConditionNormalizer.Normalize(condition), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Refurbished")]
    public void Should_return_null_for_a_missing_or_unrecognised_condition(string? condition)
    {
        Assert.That(MercariConditionNormalizer.Normalize(condition), Is.Null);
    }

    [Test]
    public void Should_expose_every_normalized_option_as_taxonomy_criteria()
    {
        var question = MercariConditionNormalizer.ToTaxonomyQuestion();

        Assert.Multiple(() =>
        {
            Assert.That(question.Key, Is.EqualTo("mercari_condition"));
            Assert.That(question.Criteria.Keys, Is.EquivalentTo(["new", "like_new", "good", "fair", "poor"]));
        });
    }
}
