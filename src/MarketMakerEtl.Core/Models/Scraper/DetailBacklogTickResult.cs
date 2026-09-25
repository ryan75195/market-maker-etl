using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record DetailBacklogTickResult(
    int Selected,
    int Attempted,
    int Succeeded,
    IReadOnlyList<ScrapeRunIssueDetails> Failures);
