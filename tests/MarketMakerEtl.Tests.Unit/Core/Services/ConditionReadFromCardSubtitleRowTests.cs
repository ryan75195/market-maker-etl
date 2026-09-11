using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ConditionReadFromCardSubtitleRowTests
{
    private const string CardWithConditionInSubtitleRow = """
        <ul class="srp-results">
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/123456789012?hash=item1">link</a>
            <div class="s-card__title"><span class="su-styled-text primary default">Sony PlayStation 5 Slim</span></div>
            <div class="s-card__subtitle-row">
              <div class="s-card__subtitle">
                <span class="su-styled-text secondary default">Pre-owned</span>
              </div>
            </div>
            <div class="s-card__attribute-row">
              <span class="su-styled-text primary bold large-1 s-card__price">£249.99</span>
            </div>
          </li>
        </ul>
        """;

    [Test]
    public void Should_read_condition_from_the_card_subtitle_row()
    {
        var summaries = new EbaySearchParser().Parse(CardWithConditionInSubtitleRow);

        Assert.That(summaries.Single().Condition, Is.EqualTo("Pre-owned"));
    }
}
