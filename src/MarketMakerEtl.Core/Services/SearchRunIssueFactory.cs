using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

internal static class SearchRunIssueFactory
{
    internal static void AddCapHitIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        bool sold,
        PriceBandCollectionSummary? summary)
    {
        if (summary is not { CapHit: true })
        {
            return;
        }

        var direction = sold ? "sold" : "active";
        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "PriceBandCapHit",
            ErrorMessage: $"Hit the {summary.BandsFetched}-band cap while collecting '{searchTerm}' ({direction}).",
            Phase: "Search",
            HttpStatusCode: null));
    }

    internal static void AddNoResultsIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        bool sold,
        PriceBandCollectionSummary? summary,
        int listingsAdded)
    {
        if (summary is not { BandsFetched: > 0, TotalReported: null } || listingsAdded > 0)
        {
            return;
        }

        var direction = sold ? "sold" : "active";
        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SearchYieldedNoResults",
            ErrorMessage: $"'{searchTerm}' ({direction}) fetched {summary.BandsFetched} band(s), collected 0 listings, and the search response carried no reported total; the scraper likely returned an unexpected payload shape rather than a genuinely empty search.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    internal static void AddBackfillWindowIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        int soldBackfillDays,
        PriceBandCollectionSummary? summary)
    {
        if (summary?.BackfillCutoffUtc is not { } cutoff)
        {
            return;
        }

        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SoldBackfillWindow",
            ErrorMessage: $"Sold backfill for '{searchTerm}' covered the last {soldBackfillDays} day(s); cutoff {cutoff:O}.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    internal static void AddBackfillOverflowIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        PriceBandCollectionSummary? summary)
    {
        if (summary is not { BandsOverCapacityUnsplit: > 0 })
        {
            return;
        }

        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SoldBackfillBandOverflow",
            ErrorMessage: $"{summary.BandsOverCapacityUnsplit} band(s) reported over 100 in-window sold listings for '{searchTerm}' but could not be split further; only the available page was stored.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    internal static void AddBackfillBudgetExhaustedIssue(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        PriceBandCollectionSummary? summary)
    {
        if (summary is not { BackfillBudgetExhausted: true })
        {
            return;
        }

        issues.Add(new ScrapeRunIssueDetails(
            ListingId: null,
            IssueType: "SoldBackfillBudgetExhausted",
            ErrorMessage: $"Sold backfill for '{searchTerm}' exhausted its item-page fetch budget before every listing's sale date could be resolved; only conservatively-decided listings were stored.",
            Phase: "Search",
            HttpStatusCode: null));
    }

    internal static void AddSearchPageFailedIssues(
        List<ScrapeRunIssueDetails> issues,
        string searchTerm,
        bool sold,
        PriceBandCollectionSummary? summary)
    {
        if (summary?.SearchPageFailures is not { Count: > 0 } failures)
        {
            return;
        }

        var direction = sold ? "sold" : "active";

        foreach (var failure in failures)
        {
            var range = $"{FormatPrice(failure.MinPrice)}-{FormatPrice(failure.MaxPrice)}";
            issues.Add(new ScrapeRunIssueDetails(
                ListingId: null,
                IssueType: "SearchPageFailed",
                ErrorMessage: $"Search page fetch for '{searchTerm}' ({direction}) band [{range}] failed after repeated attempts: {failure.ErrorMessage}",
                Phase: "Search",
                HttpStatusCode: null));
        }
    }

    private static string FormatPrice(decimal? value) => value?.ToString("0.##") ?? "open";

    internal static void ThrowIfListingMarkupProducedNoResults(ISearchPageParser parser, string html)
    {
        if (!parser.ContainsListingMarkup(html))
        {
            return;
        }

        throw new InvalidOperationException(
            "Search page contained listing markup but produced no parsed listings.");
    }
}
