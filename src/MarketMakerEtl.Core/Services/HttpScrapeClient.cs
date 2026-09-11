using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class HttpScrapeClient : IScrapeClient
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private readonly HttpClient _http;
    private readonly ScrapeClientOptions _options;
    private readonly ILogger<HttpScrapeClient> _logger;

    public HttpScrapeClient(
        HttpClient http,
        ScrapeClientOptions options,
        ILogger<HttpScrapeClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetPageHtml(string url, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_options.FetchTimeout);

        var jobId = await StartJob(url, deadline.Token);
        await WaitForTerminal(jobId, deadline.Token);
        var item = await GetFirstResult(jobId, deadline.Token);
        var html = await DownloadContent(item, url, deadline.Token);

        _logger.LogDebug("Fetched {Url} ({Length} bytes)", url, html.Length);
        return html;
    }

    private async Task<string> StartJob(string url, CancellationToken ct)
    {
        var request = new ScrapeJobRequest([url]);
        var response = await _http.PostAsJsonAsync(BuildUri("api/NewJob"), request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ScrapeJobResponse>(JsonOptions, ct);
        return body?.JobId ?? throw new InvalidOperationException("Scraper returned no job id");
    }

    private async Task WaitForTerminal(string jobId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var status = await GetStatus(jobId, ct);

            if (status == ScrapeJobStatus.Failure)
            {
                throw new InvalidOperationException($"Scrape job {jobId} failed");
            }

            if (status == ScrapeJobStatus.Success)
            {
                return;
            }

            await Task.Delay(_options.PollInterval, ct);
        }
    }

    private async Task<ScrapeJobStatus> GetStatus(string jobId, CancellationToken ct)
    {
        var uri = BuildUri($"api/GetStatus?jobId={Uri.EscapeDataString(jobId)}");
        var response = await _http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ScrapeJobStatusResponse>(JsonOptions, ct);
        return body?.Job?.Status ?? ScrapeJobStatus.Unknown;
    }

    private async Task<ScrapeJobItem> GetFirstResult(string jobId, CancellationToken ct)
    {
        var uri = BuildUri($"api/GetResults?jobId={Uri.EscapeDataString(jobId)}");
        var response = await _http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<ScrapeJobItem>>(JsonOptions, ct);
        return items is { Count: > 0 }
            ? items[0]
            : throw new InvalidOperationException($"No results returned for job {jobId}");
    }

    private async Task<string> DownloadContent(ScrapeJobItem item, string url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.BlobUri))
        {
            throw new InvalidOperationException($"Scrape returned no content for {url}: {item.Error}");
        }

        return await _http.GetStringAsync(item.BlobUri, ct);
    }

    private string BuildUri(string relativePath)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/{relativePath}";
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
