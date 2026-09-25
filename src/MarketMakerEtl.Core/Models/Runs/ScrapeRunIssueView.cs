namespace MarketMakerEtl.Core.Models.Runs;

public sealed record ScrapeRunIssueView(
    int Id,
    int ScrapeRunId,
    string? ListingId,
    string IssueType,
    string ErrorMessage,
    string Phase,
    int? HttpStatusCode,
    DateTime CreatedUtc,
    string Severity);
