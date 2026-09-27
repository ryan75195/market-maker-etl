using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record DetailBacklogTickResult(
    int Selected,
    int Attempted,
    int Succeeded,
    IReadOnlyList<ScrapeRunIssueDetails> Failures,
    int FamilyAttempted = 0,
    int FamilyRemaining = 0,
    int FamilyInScopeFetched = 0,
    int FamilyUnclassifiedFetched = 0,
    int FamilyInScopeRemaining = 0);
