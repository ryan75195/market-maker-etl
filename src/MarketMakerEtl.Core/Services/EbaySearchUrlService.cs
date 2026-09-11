using System.Globalization;
using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Core.Services;

public sealed class EbaySearchUrlService : IEbaySearchUrlService
{
    private const string SearchBase = "https://www.ebay.co.uk/sch/i.html";

    public string BuildSearch(string searchTerm, bool sold, int page)
    {
        var term = Uri.EscapeDataString(searchTerm);
        var soldFilter = sold ? "&LH_Sold=1&LH_Complete=1" : string.Empty;
        var pageNumber = Math.Max(1, page);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SearchBase}?_nkw={term}&_sacat=0{soldFilter}&_pgn={pageNumber}");
    }
}
