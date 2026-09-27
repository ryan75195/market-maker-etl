using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class FetcherHealthClient : IFetcherHealthClient
{
    private const string OkStatus = "ok";
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly FetcherOptions _options;

    public FetcherHealthClient(HttpClient http, FetcherOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<bool> CheckSidecarReachable(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(HealthCheckTimeout);

        try
        {
            return await SendHealthCheck(deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<bool> SendHealthCheck(CancellationToken ct)
    {
        var response = await _http.GetAsync(BuildUri("health"), ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadFromJsonAsync<FetcherHealthPayload>(JsonOptions, ct);
        return string.Equals(body?.Status, OkStatus, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildUri(string relativePath) => $"{_options.BaseUrl.TrimEnd('/')}/{relativePath}";

    private sealed record FetcherHealthPayload([property: JsonPropertyName("status")] string? Status);
}
