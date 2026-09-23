using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariSearchPayloadCarriesImagesOriginalPriceAndCategoryTests
{
    private static readonly string CapturedPayload = File.ReadAllText(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", "search-payload.json"));

    [Test]
    public void Should_read_the_photo_url_original_price_and_category_from_a_captured_search_payload_item()
    {
        var summary = new MercariSearchParser().Parse(CapturedPayload)[0];

        Assert.Multiple(() =>
        {
            Assert.That(
                summary.ImageUrls,
                Is.EqualTo(new[] { "https://u-mercari-images.mercdn.net/photos/m71344610988_1.jpg?1790200178" }));
            Assert.That(summary.OriginalPrice, Is.EqualTo(399.00m));
            Assert.That(summary.Category, Is.EqualTo("Consoles"));
        });
    }

    [Test]
    public void Should_leave_likes_unset_because_the_search_payload_never_reports_a_like_count()
    {
        var summaries = new MercariSearchParser().Parse(CapturedPayload);

        Assert.That(summaries.Select(summary => summary.Likes), Has.All.Null);
    }
}
