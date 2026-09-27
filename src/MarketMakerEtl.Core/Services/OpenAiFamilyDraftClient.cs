using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

public sealed class OpenAiFamilyDraftClient : IFamilyDraftClient
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly OnboardingOptions _draftOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenAiFamilyDraftClient> _logger;

    public OpenAiFamilyDraftClient(
        HttpClient http,
        OpenAiOptions options,
        OnboardingOptions draftOptions,
        TimeProvider timeProvider,
        ILogger<OpenAiFamilyDraftClient> logger)
    {
        _http = http;
        _options = options;
        _draftOptions = draftOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FamilyDraftResult> DraftTaxonomy(FamilyDraftPrompt prompt, CancellationToken ct)
    {
        var messages = FamilyDraftPromptBuilder.Build(prompt);
        var body = FamilyDraftRequestBuilder.Build(_draftOptions.DraftModel, _draftOptions.DraftReasoningEffort, messages);
        var completion = await OpenAiChatCompletionSender.Send(_http, DraftSenderOptions(), _timeProvider, body, ct);
        var parsed = FamilyDraftResponseParser.Parse(completion.Content);
        var costUsd = ComputeCost(completion.PromptTokens, completion.CompletionTokens);

        _logger.LogInformation(
            "Drafted taxonomy for search term {SearchTerm} using {PromptTokens} prompt + {CompletionTokens} completion tokens (~${CostUsd:F4}).",
            prompt.SearchTerm,
            completion.PromptTokens,
            completion.CompletionTokens,
            costUsd);

        return new FamilyDraftResult(
            parsed.TaxonomyJson, parsed.DealGroupBy, completion.PromptTokens, completion.CompletionTokens, costUsd);
    }

    private OpenAiOptions DraftSenderOptions() =>
        _options with { Model = _draftOptions.DraftModel, TimeoutSeconds = _draftOptions.DraftTimeoutSeconds };

    private decimal ComputeCost(int promptTokens, int completionTokens) =>
        promptTokens / 1_000_000m * _draftOptions.DraftInputCostPerMillionUsd
            + completionTokens / 1_000_000m * _draftOptions.DraftOutputCostPerMillionUsd;
}
