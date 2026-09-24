using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal readonly record struct SearchPageFailure(decimal? MinPrice, decimal? MaxPrice, string ErrorMessage);

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

    internal async Task<SearchPageFetchOutcome> Fetch(string url, PriceBand band, CancellationToken ct)
    {
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var html = await _client.GetPageHtml(url, ct);
                return SearchPageFetchOutcome.Success(_parser.Parse(html));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                LogAttemptFailed(band, attempt, ex);

                if (attempt < _maxAttempts)
                {
                    await Task.Delay(_retryDelay, ct);
                }
            }
        }

        var failure = new SearchPageFailure(band.MinPrice, band.MaxPrice, InnermostMessage(lastFailure!));
        return SearchPageFetchOutcome.Failed(failure);
    }

    private void LogAttemptFailed(PriceBand band, int attempt, Exception ex) =>
        _logger.LogWarning(
            ex,
            "Search page fetch failed for band [{MinPrice}-{MaxPrice}] attempt {Attempt}/{MaxAttempts}.",
            band.MinPrice, band.MaxPrice, attempt, _maxAttempts);

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
