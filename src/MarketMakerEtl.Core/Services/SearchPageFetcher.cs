using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal enum SearchPageFailureKind
{
    Exception,
    EmptyResult,
}

internal readonly record struct SearchPageFailure(
    decimal? MinPrice,
    decimal? MaxPrice,
    string ErrorMessage,
    SearchPageFailureKind Kind = SearchPageFailureKind.Exception,
    int? ParentReportedCount = null);

internal readonly record struct SearchPageFetchOutcome(SearchPageResult? Result, SearchPageFailure? Failure)
{
    internal static SearchPageFetchOutcome Success(SearchPageResult result) => new(result, null);

    internal static SearchPageFetchOutcome Failed(SearchPageFailure failure) => new(null, failure);
}

internal sealed class SearchPageFetcher
{
    private readonly IScrapeClient _client;
    private readonly ISearchPageParser _parser;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly ILogger _logger;

    internal SearchPageFetcher(
        IScrapeClient client, ISearchPageParser parser, int maxAttempts, TimeSpan retryDelay, ILogger logger)
    {
        _client = client;
        _parser = parser;
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelay = retryDelay;
        _logger = logger;
    }

    internal async Task<SearchPageFetchOutcome> Fetch(
        string url, PriceBand band, int? parentReportedCount, CancellationToken ct)
    {
        var retryEmpty = parentReportedCount is > 0;
        var lastKind = SearchPageFailureKind.EmptyResult;
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var parsed = _parser.Parse(await _client.GetPageHtml(url, ct));
                if (!retryEmpty || !IsEmpty(parsed))
                {
                    return SearchPageFetchOutcome.Success(parsed);
                }

                lastKind = SearchPageFailureKind.EmptyResult;
                LogEmptyAttempt(band, attempt, parentReportedCount!.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastKind = SearchPageFailureKind.Exception;
                lastFailure = ex;
                LogAttemptFailed(band, attempt, ex);
            }

            if (attempt < _maxAttempts)
            {
                await Task.Delay(_retryDelay, ct);
            }
        }

        return BuildFailure(band, lastKind, lastFailure, parentReportedCount);
    }

    private static SearchPageFetchOutcome BuildFailure(
        PriceBand band, SearchPageFailureKind lastKind, Exception? lastFailure, int? parentReportedCount)
    {
        if (lastKind == SearchPageFailureKind.Exception)
        {
            return SearchPageFetchOutcome.Failed(new SearchPageFailure(
                band.MinPrice, band.MaxPrice, InnermostMessage(lastFailure!), SearchPageFailureKind.Exception));
        }

        return SearchPageFetchOutcome.Failed(new SearchPageFailure(
            band.MinPrice, band.MaxPrice, string.Empty, SearchPageFailureKind.EmptyResult, parentReportedCount));
    }

    private static bool IsEmpty(SearchPageResult result) =>
        result.Listings.Count == 0 && result.TotalCount is null or 0;

    private void LogAttemptFailed(PriceBand band, int attempt, Exception ex) =>
        _logger.LogWarning(
            ex,
            "Search page fetch failed for band [{MinPrice}-{MaxPrice}] attempt {Attempt}/{MaxAttempts}.",
            band.MinPrice, band.MaxPrice, attempt, _maxAttempts);

    private void LogEmptyAttempt(PriceBand band, int attempt, int parentReportedCount) =>
        _logger.LogWarning(
            "Search page for band [{MinPrice}-{MaxPrice}] came back empty on attempt {Attempt}/{MaxAttempts} "
                + "despite its parent band reporting {ParentReportedCount} result(s).",
            band.MinPrice, band.MaxPrice, attempt, _maxAttempts, parentReportedCount);

    private static string InnermostMessage(Exception exception)
    {
        var current = exception;

        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
