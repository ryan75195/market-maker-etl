namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ScrapeRunIssueDetails(
    string? ListingId,
    string IssueType,
    string ErrorMessage,
    string Phase,
    int? HttpStatusCode);
