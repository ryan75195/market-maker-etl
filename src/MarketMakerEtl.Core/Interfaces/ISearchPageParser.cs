using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface ISearchPageParser
{
    IReadOnlyList<ListingSummary> Parse(string html);
}
