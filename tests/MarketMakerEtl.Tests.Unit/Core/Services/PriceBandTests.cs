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

    [Test]
    public void Should_seed_nine_geometric_bands()
    {
        var seeds = PriceBand.SeedBands();

        Assert.That(seeds, Has.Count.EqualTo(9));
    }

    [Test]
    public void Should_start_the_first_seed_band_at_zero_and_leave_the_last_open_ended()
    {
        var seeds = PriceBand.SeedBands();

        Assert.Multiple(() =>
        {
            Assert.That(seeds[0].MinPrice, Is.EqualTo(0m));
            Assert.That(seeds[^1].MaxPrice, Is.Null);
        });
    }

    [Test]
    public void Should_keep_adjacent_seed_bands_exactly_one_cent_apart()
    {
        var seeds = PriceBand.SeedBands();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < seeds.Count - 1; i++)
            {
                Assert.That(seeds[i].MaxPrice, Is.Not.Null);
                Assert.That(seeds[i + 1].MinPrice, Is.EqualTo(seeds[i].MaxPrice!.Value + 0.01m));
            }
        });
    }

    [Test]
    public void Should_cover_the_range_below_one_hundred_dollars_within_the_first_five_seed_bands()
    {
        var seeds = PriceBand.SeedBands();

        Assert.That(seeds[4].MaxPrice, Is.EqualTo(100m));
    }
}
