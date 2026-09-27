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

    public static async Task<OpenAiCompletionResult> Send(
        HttpClient http, OpenAiOptions options, TimeProvider timeProvider, JsonObject body, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        for (var attempt = 0; ; attempt++)
        {
            var outcome = await Attempt(http, options, body, deadline, ct);
            if (outcome.Result is not null)
            {
                return outcome.Result;
            }

            if (attempt >= MaxAttempts - 1 || !outcome.Retryable)
            {
                throw outcome.Failure!;
            }

            await Delay(ComputeBackoff(attempt), timeProvider, deadline, options, ct);
        }
    }

    private static async Task<AttemptOutcome> Attempt(
        HttpClient http, OpenAiOptions options, JsonObject body, CancellationTokenSource deadline, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await PostOnce(http, options, body, deadline.Token);
        }
        catch (HttpRequestException ex)
        {
            return AttemptOutcome.Failed(
                new ListingClassifierException($"OpenAI request failed: {ex.Message}", ex), retryable: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AttemptOutcome.Failed(BuildTimeout(options), retryable: false);
        }

        if (response.IsSuccessStatusCode)
        {
            var envelope = await ReadResponseBody(response, deadline, options, ct);
            return AttemptOutcome.Succeeded(ExtractResult(envelope));
        }

        var errorBody = await TryReadBody(response, deadline.Token);
        return AttemptOutcome.Failed(
            new ListingClassifierException($"OpenAI returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}"),
            retryable: IsRetryable(response.StatusCode));
    }

    private static async Task<string> ReadResponseBody(
        HttpResponseMessage response, CancellationTokenSource deadline, OpenAiOptions options, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw BuildTimeout(options);
        }
    }

    private static async Task Delay(
        TimeSpan delay, TimeProvider timeProvider, CancellationTokenSource deadline, OpenAiOptions options, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, timeProvider, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw BuildTimeout(options);
        }
    }

    private static async Task<HttpResponseMessage> PostOnce(
        HttpClient http, OpenAiOptions options, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return await http.SendAsync(request, ct);
    }

    private static ListingClassifierException BuildTimeout(OpenAiOptions options) =>
        ListingClassifierException.Timeout($"OpenAI request timed out after {options.TimeoutSeconds}s.");

    private static OpenAiCompletionResult ExtractResult(string envelope)
    {
        try
        {
            using var document = JsonDocument.Parse(envelope);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return content is null
                ? throw new ListingClassifierException("OpenAI response message content was null.")
                : new OpenAiCompletionResult(content, ReadUsageTokens(document.RootElement, "prompt_tokens"),
                    ReadUsageTokens(document.RootElement, "completion_tokens"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new ListingClassifierException("OpenAI response was not a valid chat completion.", ex);
        }
    }

    private static int ReadUsageTokens(JsonElement root, string propertyName) =>
        root.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;

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

    private sealed class AttemptOutcome
    {
        public OpenAiCompletionResult? Result { get; private init; }

        public ListingClassifierException? Failure { get; private init; }

        public bool Retryable { get; private init; }

        public static AttemptOutcome Succeeded(OpenAiCompletionResult result) => new() { Result = result };

        public static AttemptOutcome Failed(ListingClassifierException failure, bool retryable) =>
            new() { Failure = failure, Retryable = retryable };
    }
}
