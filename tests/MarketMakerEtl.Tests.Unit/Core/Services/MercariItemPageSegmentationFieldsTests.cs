using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class MercariItemPageSegmentationFieldsTests
{
    private static readonly string ElectronicsItemPage = ReadFixture("item-active-m71344610988.html");
    private static readonly string ClothingItemPage = ReadFixture("item-active-m74693959349.html");
    private static readonly string ToysItemPage = ReadFixture("item-active-m14283608971.html");

    [Test]
    public void Should_read_leaf_category_brand_condition_and_discount_ratio_from_an_item_page()
    {
        var listing = new MercariItemPageParser().Parse(ElectronicsItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.CategoryId, Is.EqualTo(797));
            Assert.That(listing.CategoryHierarchy!.Level2Id, Is.EqualTo(797));
            Assert.That(listing.CategoryHierarchy.Level2Name, Is.EqualTo("Consoles"));
            Assert.That(listing.CategoryHierarchy.Level0Id, Is.Null);
            Assert.That(listing.CategoryHierarchy.Level1Id, Is.Null);
            Assert.That(listing.BrandId, Is.EqualTo(5058));
            Assert.That(listing.ConditionId, Is.EqualTo(3));
            Assert.That(listing.DiscountRatio, Is.EqualTo(3));
            Assert.That(listing.ShipsFromState, Is.EqualTo("Pennsylvania"));
            Assert.That(listing.ShippingPayer, Is.EqualTo("seller"));
            Assert.That(listing.RawJson, Does.Contain("m71344610988").Or.Contain("Consoles"));
        });
    }

    [Test]
    public void Should_read_the_sellers_profile_from_an_item_page()
    {
        var listing = new MercariItemPageParser().Parse(ElectronicsItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.SellerProfile, Is.Not.Null);
            Assert.That(listing.SellerProfile!.SellerId, Is.EqualTo(865070813L));
            Assert.That(listing.SellerProfile.Name, Is.EqualTo("Nerd Mom Electronics"));
            Assert.That(listing.SellerProfile.NumSales, Is.EqualTo(317));
            Assert.That(listing.SellerProfile.NumSellItems, Is.EqualTo(328));
            Assert.That(listing.SellerProfile.RatingCount, Is.EqualTo(327));
            Assert.That(listing.SellerProfile.RatingAverage, Is.EqualTo(5));
            Assert.That(listing.SellerProfile.IsProSeller, Is.False);
            Assert.That(
                listing.SellerProfile.AccountCreatedUtc,
                Is.EqualTo(new DateTimeOffset(2021, 8, 26, 0, 54, 4, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Should_read_the_size_name_from_a_clothing_item_page()
    {
        var listing = new MercariItemPageParser().Parse(ClothingItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.That(listing!.SizeName, Is.EqualTo("6 (39)"));
    }

    [Test]
    public void Should_read_a_buyer_paid_shipping_payer_and_ships_from_state_from_a_toy_item_page()
    {
        var listing = new MercariItemPageParser().Parse(ToysItemPage);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listing!.ShippingPayer, Is.EqualTo("buyer"));
            Assert.That(listing.ShipsFromState, Is.EqualTo("Hawaii"));
            Assert.That(listing.DiscountRatio, Is.EqualTo(6));
        });
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Mercari", fileName));
}
