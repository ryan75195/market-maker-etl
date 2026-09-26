using MarketMakerEtl.Api;

namespace MarketMakerEtl.Tests.Unit.Api;

[TestFixture]
public class ReviewDescriptionTruncatorTests
{
    [Test]
    public void Should_return_null_when_the_description_is_null()
    {
        var result = ReviewDescriptionTruncator.Truncate(null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Should_return_the_description_unchanged_when_it_is_within_the_limit()
    {
        var description = new string('a', ReviewDescriptionTruncator.MaxLength);

        var result = ReviewDescriptionTruncator.Truncate(description);

        Assert.That(result, Is.EqualTo(description));
    }

    [Test]
    public void Should_truncate_a_description_longer_than_the_limit()
    {
        var description = new string('a', ReviewDescriptionTruncator.MaxLength + 500);

        var result = ReviewDescriptionTruncator.Truncate(description);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.EqualTo(ReviewDescriptionTruncator.MaxLength));
            Assert.That(result, Is.EqualTo(new string('a', ReviewDescriptionTruncator.MaxLength)));
        });
    }
}
