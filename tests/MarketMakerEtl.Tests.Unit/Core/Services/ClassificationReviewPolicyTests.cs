using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ClassificationReviewPolicyTests
{
    [Test]
    public void Should_need_review_when_confidence_is_just_below_the_threshold()
    {
        var row = BuildRow(ClassificationSource.Model, isApplicable: true, confidence: 0.8999);

        var result = ClassificationReviewPolicy.NeedsReview(row, 0.9);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Should_not_need_review_when_confidence_is_at_the_threshold()
    {
        var row = BuildRow(ClassificationSource.Model, isApplicable: true, confidence: 0.9);

        var result = ClassificationReviewPolicy.NeedsReview(row, 0.9);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Should_not_need_review_for_a_human_row()
    {
        var row = BuildRow(ClassificationSource.Human, isApplicable: true, confidence: 0.1);

        var result = ClassificationReviewPolicy.NeedsReview(row, 0.9);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Should_not_need_review_for_a_non_applicable_row()
    {
        var row = BuildRow(ClassificationSource.Model, isApplicable: false, confidence: 0.1);

        var result = ClassificationReviewPolicy.NeedsReview(row, 0.9);

        Assert.That(result, Is.False);
    }

    private static ListingClassificationEntity BuildRow(ClassificationSource source, bool isApplicable, double confidence) =>
        new()
        {
            Question = "item_type",
            Choice = "console",
            ResolvedChoice = "console",
            IsApplicable = isApplicable,
            Confidence = confidence,
            Agreement = 1.0,
            ProbabilitiesJson = "{}",
            Source = source,
            ClassifiedUtc = DateTime.UtcNow
        };
}
