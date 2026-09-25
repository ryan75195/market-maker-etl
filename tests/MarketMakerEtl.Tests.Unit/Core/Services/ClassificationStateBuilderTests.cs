using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ClassificationStateBuilderTests
{
    [Test]
    public void Should_join_category_names_into_a_mercari_category_path()
    {
        var target = new ListingClassificationTarget(1, "Title", "Games", "Accessories", "Controllers", "Sony", "Desc");

        var state = ClassificationStateBuilder.BuildState(target);

        Assert.Multiple(() =>
        {
            Assert.That(state.Title, Is.EqualTo("Title"));
            Assert.That(state.MercariCategory, Is.EqualTo("Games > Accessories > Controllers"));
            Assert.That(state.Brand, Is.EqualTo("Sony"));
            Assert.That(state.Description, Is.EqualTo("Desc"));
        });
    }

    [Test]
    public void Should_return_a_null_category_when_no_category_names_are_present()
    {
        var target = new ListingClassificationTarget(1, "Title", null, null, null, null, null);

        var state = ClassificationStateBuilder.BuildState(target);

        Assert.That(state.MercariCategory, Is.Null);
    }

    [Test]
    public void Should_truncate_a_long_description()
    {
        var longDescription = new string('a', 1300);
        var target = new ListingClassificationTarget(1, "Title", null, null, null, null, longDescription);

        var state = ClassificationStateBuilder.BuildState(target);

        Assert.That(state.Description, Has.Length.EqualTo(1200));
    }
}
