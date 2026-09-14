using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ScrapeRunWork(
    int RunId,
    int JobId,
    string SearchTerm,
    Marketplace Marketplace = Marketplace.Ebay);
