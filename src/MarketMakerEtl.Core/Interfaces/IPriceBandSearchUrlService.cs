namespace MarketMakerEtl.Core.Interfaces;

public interface IPriceBandSearchUrlService
{
    string BuildSearch(string searchTerm, bool sold, decimal? minPrice, decimal? maxPrice);
}
