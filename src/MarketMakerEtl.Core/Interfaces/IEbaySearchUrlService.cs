namespace MarketMakerEtl.Core.Interfaces;

public interface IEbaySearchUrlService
{
    string BuildSearch(string searchTerm, bool sold, int page);
}
