using System.Net.Http.Json;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class HttpDealWebhookClient : IDealWebhookClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly DealsOptions _options;
    private readonly ILogger<HttpDealWebhookClient> _logger;

    public HttpDealWebhookClient(HttpClient http, DealsOptions options, ILogger<HttpDealWebhookClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task Notify(DealWebhookPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RequestTimeout);

        try
        {
            var response = await _http.PostAsJsonAsync(_options.WebhookUrl, payload, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Deal webhook returned {StatusCode} for {WebhookUrl}",
                    (int)response.StatusCode,
                    _options.WebhookUrl);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Deal webhook to {WebhookUrl} timed out after {TimeoutSeconds}s",
                _options.WebhookUrl,
                RequestTimeout.TotalSeconds);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Deal webhook to {WebhookUrl} failed", _options.WebhookUrl);
        }
    }
}
