using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class SoldDateParserTests
{
    [Test]
    public void Should_parse_an_iso8601_utc_timestamp_as_utc()
    {
        var parsed = SoldDateParser.Parse("2026-09-23T22:21:59Z");

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value, Is.EqualTo(new DateTime(2026, 9, 23, 22, 21, 59, DateTimeKind.Utc)));
            Assert.That(parsed.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void Should_parse_the_short_weekday_ebay_sold_date_format()
    {
        var parsed = SoldDateParser.Parse("Fri, 11 Sep at 6:09 PM");

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Month, Is.EqualTo(9));
            Assert.That(parsed.Value.Day, Is.EqualTo(11));
            Assert.That(parsed.Value.Hour, Is.EqualTo(18));
            Assert.That(parsed.Value.Minute, Is.EqualTo(9));
        });
    }

    [Test]
    public void Should_parse_the_zero_padded_weekday_ebay_sold_date_format()
    {
        var parsed = SoldDateParser.Parse("Wed, 09 Sep at 6:09 PM");

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Month, Is.EqualTo(9));
            Assert.That(parsed.Value.Day, Is.EqualTo(9));
            Assert.That(parsed.Value.Hour, Is.EqualTo(18));
            Assert.That(parsed.Value.Minute, Is.EqualTo(9));
        });
    }

    [Test]
    public void Should_parse_the_long_month_name_ebay_sold_date_format()
    {
        var parsed = SoldDateParser.Parse("11 September 2026");

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Year, Is.EqualTo(2026));
            Assert.That(parsed.Value.Month, Is.EqualTo(9));
            Assert.That(parsed.Value.Day, Is.EqualTo(11));
        });
    }

    [Test]
    public void Should_parse_the_short_month_name_ebay_sold_date_format()
    {
        var parsed = SoldDateParser.Parse("11 Sep 2026");

        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Value.Year, Is.EqualTo(2026));
            Assert.That(parsed.Value.Month, Is.EqualTo(9));
            Assert.That(parsed.Value.Day, Is.EqualTo(11));
        });
    }

    [Test]
    public void Should_return_null_for_an_unrecognised_sold_date_format()
    {
        var parsed = SoldDateParser.Parse("not-a-recognised-date");

        Assert.That(parsed, Is.Null);
    }
}
