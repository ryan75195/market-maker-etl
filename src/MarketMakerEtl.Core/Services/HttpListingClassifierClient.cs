using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

public sealed class HttpListingClassifierClient : IListingClassifierClient
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private readonly HttpClient _http;
    private readonly ClassifierOptions _options;

    public HttpListingClassifierClient(HttpClient http, ClassifierOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<ClassifyResponse> Classify(ClassifyRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            return await Send(request, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ListingClassifierException(
                $"Classifying against {_options.BaseUrl} timed out after {_options.TimeoutSeconds}s.");
        }
        catch (JsonException ex)
        {
            throw new ListingClassifierException("Classifier returned a malformed response body.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ListingClassifierException($"Classifying against {_options.BaseUrl} failed.", ex);
        }
    }

    private async Task<ClassifyResponse> Send(ClassifyRequest request, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(BuildUri("v1/classify"), request, JsonOptions, ct);
        await EnsureSuccess(response, ct);

        var body = await response.Content.ReadFromJsonAsync<ClassifyResponse>(JsonOptions, ct);
        return body ?? throw new ListingClassifierException("Classifier returned an empty response body.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await TryReadBody(response, ct);
        throw new ListingClassifierException(
            $"Classifier returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static async Task<string> TryReadBody(HttpResponseMessage response, CancellationToken ct)
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

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return options;
    }
}
