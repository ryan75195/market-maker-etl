using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class FetcherScrapeClient : IScrapeClient
{
    private const string ProxyUnavailableError = "proxy_unavailable";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly FetcherOptions _options;
    private readonly IFetchOutcomeStore _outcomeStore;
    private readonly ILogger<FetcherScrapeClient> _logger;

    public FetcherScrapeClient(
        HttpClient http, FetcherOptions options, IFetchOutcomeStore outcomeStore, ILogger<FetcherScrapeClient> logger)
    {
        _http = http;
        _options = options;
        _outcomeStore = outcomeStore;
        _logger = logger;
    }

    public async Task<string> GetPageHtml(string url, CancellationToken ct)
    {
        try
        {
            var body = await FetchPage(url, ct);
            await SafeRecordOutcome(FetchOutcomeKind.Success, ct);
            return body;
        }
        catch (ListingNotFoundException)
        {
            await SafeRecordOutcome(FetchOutcomeKind.NotFound, ct);
            throw;
        }
        catch (FetchInfrastructureUnavailableException)
        {
            await SafeRecordOutcome(FetchOutcomeKind.Infrastructure, ct);
            throw;
        }
        catch (FetchFailedException)
        {
            await SafeRecordOutcome(FetchOutcomeKind.Other, ct);
            throw;
        }
    }

    private async Task SafeRecordOutcome(FetchOutcomeKind kind, CancellationToken ct)
    {
        try
        {
            await _outcomeStore.RecordOutcome(kind, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to record fetch outcome {Kind}.", kind);
        }
    }

    private async Task<string> FetchPage(string url, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_options.Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(BuildUri("fetch"), new FetchRequest(url), JsonOptions, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FetchInfrastructureUnavailableException(
                $"Fetching {url} via the fetcher sidecar at {_options.BaseUrl} timed out after "
                + $"{_options.Timeout.TotalSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new FetchInfrastructureUnavailableException(
                $"Could not reach the fetcher sidecar at {_options.BaseUrl}.", ex);
        }

        return await HandleResponse(url, response, deadline.Token);
    }

    private static async Task<string> HandleResponse(string url, HttpResponseMessage response, CancellationToken ct) =>
        response.StatusCode switch
        {
            HttpStatusCode.OK => await ReadBody(url, response, ct),
            HttpStatusCode.NotFound => throw new ListingNotFoundException(
                $"The fetcher sidecar reported {url} as not found."),
            HttpStatusCode.BadGateway => await HandleBadGateway(url, response, ct),
            HttpStatusCode.ServiceUnavailable => throw new FetchInfrastructureUnavailableException(
                $"The fetcher sidecar reported the upstream as blocked while fetching {url}."),
            _ => throw new FetchFailedException(
                $"The fetcher sidecar returned {(int)response.StatusCode} {response.ReasonPhrase} fetching "
                + $"{url}: {await TryReadRawBody(response, ct)}"),
        };

    private static async Task<string> HandleBadGateway(string url, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await TryReadRawBody(response, ct);

        if (string.Equals(TryReadErrorCode(body), ProxyUnavailableError, StringComparison.Ordinal))
        {
            throw new FetchInfrastructureUnavailableException(
                $"The fetcher sidecar's proxy was unavailable while fetching {url}.");
        }

        throw new FetchFailedException($"The fetcher sidecar failed to fetch {url}: {body}");
    }

    private static async Task<string> ReadBody(string url, HttpResponseMessage response, CancellationToken ct)
    {
        FetchResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<FetchResponse>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new FetchFailedException($"The fetcher sidecar returned a malformed response body for {url}.", ex);
        }

        return payload?.Body is { Length: > 0 } body
            ? body
            : throw new FetchFailedException($"The fetcher sidecar returned an empty response body for {url}.");
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> TryReadRawBody(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private string BuildUri(string relativePath) => $"{_options.BaseUrl.TrimEnd('/')}/{relativePath}";

    private sealed record FetchRequest([property: JsonPropertyName("url")] string Url);

    private sealed record FetchResponse(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("body")] string? Body);
}
