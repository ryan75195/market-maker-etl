using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal sealed class OpenAiListingClassifierClient : IListingClassifierClient
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ClassificationReviewOptions _reviewOptions;
    private readonly IOpenAiChatCompletionSender _sender;

    public OpenAiListingClassifierClient(
        HttpClient http, OpenAiOptions options, ClassificationReviewOptions reviewOptions, IOpenAiChatCompletionSender sender)
    {
        _http = http;
        _options = options;
        _reviewOptions = reviewOptions;
        _sender = sender;
    }

    public async Task<ClassifyResponse> Classify(ClassifyRequest request, OpenAiUsagePurpose purpose, CancellationToken ct)
    {
        if (request.States.Count == 0)
        {
            return new ClassifyResponse(request.Model, 1, []);
        }

        var systemPrompt = OpenAiPromptBuilder.Build(request.Model, request.Questions, request.Guidance);
        var batches = Chunk(request.States, Math.Max(1, _options.BatchSize));
        var batchResults = await ClassifyBatches(systemPrompt, request.Questions, batches, purpose, ct);
        var byId = MergeResults(batchResults);
        var results = request.States.Select(state => ResolveResult(byId, state)).ToList();
        return new ClassifyResponse(request.Model, 1, results);
    }

    private async Task<IReadOnlyDictionary<string, ClassifyResult>[]> ClassifyBatches(
        string systemPrompt,
        IReadOnlyDictionary<string, ClassifyQuestion> questions,
        IReadOnlyList<IReadOnlyList<ClassifyListingState>> batches,
        OpenAiUsagePurpose purpose,
        CancellationToken ct)
    {
        var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        try
        {
            var tasks = batches
                .Select(batch => ClassifyBatchThrottled(throttle, systemPrompt, questions, batch, purpose, ct))
                .ToList();
            return await Task.WhenAll(tasks);
        }
        finally
        {
            throttle.Dispose();
        }
    }

    private async Task<IReadOnlyDictionary<string, ClassifyResult>> ClassifyBatchThrottled(
        SemaphoreSlim throttle,
        string systemPrompt,
        IReadOnlyDictionary<string, ClassifyQuestion> questions,
        IReadOnlyList<ClassifyListingState> batch,
        OpenAiUsagePurpose purpose,
        CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try
        {
            return await ClassifySubBatch(systemPrompt, questions, batch, purpose, ct);
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, ClassifyResult>> ClassifySubBatch(
        string systemPrompt,
        IReadOnlyDictionary<string, ClassifyQuestion> questions,
        IReadOnlyList<ClassifyListingState> batch,
        OpenAiUsagePurpose purpose,
        CancellationToken ct)
    {
        try
        {
            var body = OpenAiRequestBuilder.Build(
                _options.Model, _options.ReasoningEffort, systemPrompt, questions, batch);
            var result = await _sender.Send(_http, _options, purpose, body, ct);
            return OpenAiResponseParser.Parse(result.Content, questions, _reviewOptions.ReviewThreshold);
        }
        catch (ListingClassifierException ex)
        {
            return BuildFailureResults(batch, ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, ClassifyResult> BuildFailureResults(
        IReadOnlyList<ClassifyListingState> batch, string error)
    {
        var results = new Dictionary<string, ClassifyResult>(StringComparer.Ordinal);
        foreach (var state in batch)
        {
            results[state.Id] = new ClassifyResult(new Dictionary<string, ClassifyAnswer>(), error);
        }

        return results;
    }

    private static Dictionary<string, ClassifyResult> MergeResults(
        IReadOnlyList<IReadOnlyDictionary<string, ClassifyResult>> batchResults)
    {
        var merged = new Dictionary<string, ClassifyResult>(StringComparer.Ordinal);
        foreach (var batchResult in batchResults)
        {
            foreach (var (id, result) in batchResult)
            {
                merged[id] = result;
            }
        }

        return merged;
    }

    private static ClassifyResult ResolveResult(
        IReadOnlyDictionary<string, ClassifyResult> byId, ClassifyListingState state) =>
        byId.TryGetValue(state.Id, out var result)
            ? result
            : new ClassifyResult(new Dictionary<string, ClassifyAnswer>(), "No response received for this listing.");

    private static List<List<ClassifyListingState>> Chunk(IReadOnlyList<ClassifyListingState> states, int size)
    {
        var chunks = new List<List<ClassifyListingState>>();
        for (var i = 0; i < states.Count; i += size)
        {
            chunks.Add(states.Skip(i).Take(size).ToList());
        }

        return chunks;
    }
}
