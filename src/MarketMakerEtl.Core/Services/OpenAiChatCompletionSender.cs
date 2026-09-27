using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiChatCompletionSender
{
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";
    private const int MaxAttempts = 4;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMilliseconds(500);

    public static async Task<string> Send(
        HttpClient http, OpenAiOptions options, TimeProvider timeProvider, JsonObject body, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        for (var attempt = 0; ; attempt++)
        {
            var response = await PostOnce(http, options, body, deadline, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadAsStringAsync(deadline.Token);
                return ExtractMessageContent(envelope);
            }

            if (!IsRetryable(response.StatusCode) || attempt >= MaxAttempts - 1)
            {
                var errorBody = await TryReadBody(response, deadline.Token);
                throw new ListingClassifierException(
                    $"OpenAI returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
            }

            await Task.Delay(ComputeBackoff(attempt), timeProvider, deadline.Token);
        }
    }

    private static async Task<HttpResponseMessage> PostOnce(
        HttpClient http, OpenAiOptions options, JsonObject body, CancellationTokenSource deadline, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            return await http.SendAsync(request, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw ListingClassifierException.Timeout($"OpenAI request timed out after {options.TimeoutSeconds}s.");
        }
    }

    private static string ExtractMessageContent(string envelope)
    {
        try
        {
            using var document = JsonDocument.Parse(envelope);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return content ?? throw new ListingClassifierException("OpenAI response message content was null.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new ListingClassifierException("OpenAI response was not a valid chat completion.", ex);
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        (int)statusCode == 429 || (int)statusCode >= 500;

    private static TimeSpan ComputeBackoff(int attempt) => BaseBackoff * Math.Pow(2, attempt);

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
}
