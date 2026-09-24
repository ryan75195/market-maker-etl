using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class PriceBandTests
{
    [Test]
    public void Should_preserve_open_bounds_when_splitting_an_unfiltered_band()
    {
        var children = PriceBand.Unfiltered.Split();

        Assert.Multiple(() =>
        {
            Assert.That(children, Has.Count.EqualTo(2));
            Assert.That(children[0].MinPrice, Is.Null);
            Assert.That(children[1].MaxPrice, Is.Null);
        });
    }

    [Test]
    public void Should_split_into_two_adjacent_non_overlapping_bands()
    {
        var band = new PriceBand(10m, 20m);

        var children = band.Split();

        Assert.Multiple(() =>
        {
            Assert.That(children, Has.Count.EqualTo(2));
            Assert.That(children[0].MinPrice, Is.EqualTo(10m));
            Assert.That(children[1].MaxPrice, Is.EqualTo(20m));
            Assert.That(children[1].MinPrice, Is.EqualTo(children[0].MaxPrice + 0.01m));
        });
    }

    [Test]
    public void Should_not_be_splittable_below_the_one_cent_floor()
    {
        var band = new PriceBand(10.00m, 10.01m);

        Assert.That(band.CanSplit(0.01m), Is.False);
    }

    [Test]
    public void Should_be_splittable_when_wider_than_the_minimum_width()
    {
        var band = new PriceBand(10.00m, 10.02m);

        Assert.That(band.CanSplit(0.01m), Is.True);
    }

    [Test]
    public void Should_treat_an_open_bound_as_always_splittable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new PriceBand(null, 10m).CanSplit(0.01m), Is.True);
            Assert.That(new PriceBand(10m, null).CanSplit(0.01m), Is.True);
        });
    }
}
