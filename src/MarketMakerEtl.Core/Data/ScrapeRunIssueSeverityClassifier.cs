namespace MarketMakerEtl.Core.Data;

internal static class ScrapeRunIssueSeverityClassifier
{
    private static readonly HashSet<string> InformationalIssueTypes = new(StringComparer.Ordinal)
    {
        "SoldBackfillWindow",
        "PriceBandCapHit"
    };

    internal static bool IsFailure(string issueType) => !InformationalIssueTypes.Contains(issueType);

    internal static string ToSeverityLabel(string issueType) => IsFailure(issueType) ? "error" : "info";
}
