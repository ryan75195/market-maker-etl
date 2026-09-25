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
    private static readonly int[] DelayMultipliers = [1, 3, 6, 12];

    private readonly IScrapeClient _client;
    private readonly ISearchPageParser _parser;
    private readonly int _maxAttempts;
    private readonly int _baseDelaySeconds;
    private readonly ILogger _logger;

    internal SearchPageFetcher(
        IScrapeClient client, ISearchPageParser parser, int maxAttempts, int baseDelaySeconds, ILogger logger)
    {
        _client = client;
        _parser = parser;
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseDelaySeconds = baseDelaySeconds;
        _logger = logger;
    }

    internal async Task<SearchPageFetchOutcome> Fetch(string url, PriceBand band, CancellationToken ct)
    {
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var parsed = _parser.Parse(await _client.GetPageHtml(url, ct));
                return SearchPageFetchOutcome.Success(parsed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                LogAttemptFailed(band, attempt, ex);
            }

            if (attempt < _maxAttempts)
            {
                await Task.Delay(ComputeDelay(attempt), ct);
            }
        }

        return SearchPageFetchOutcome.Failed(new SearchPageFailure(
            band.MinPrice, band.MaxPrice, InnermostMessage(lastFailure!)));
    }

    private TimeSpan ComputeDelay(int attemptNumber)
    {
        var multiplier = attemptNumber <= DelayMultipliers.Length
            ? DelayMultipliers[attemptNumber - 1]
            : DelayMultipliers[^1] * (1 << (attemptNumber - DelayMultipliers.Length));

        return TimeSpan.FromSeconds(_baseDelaySeconds * multiplier);
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
