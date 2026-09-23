namespace MarketMakerEtl.Core.Services;

internal static class MercariItemUrl
{
    public static string Build(string listingId) =>
        $"https://www.mercari.com/us/item/{Uri.EscapeDataString(listingId)}/";
}
