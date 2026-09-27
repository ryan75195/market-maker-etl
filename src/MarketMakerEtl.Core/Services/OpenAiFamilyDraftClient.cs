using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.Extensions.Logging;

namespace MarketMakerEtl.Core.Services;

internal sealed class OpenAiFamilyDraftClient : IFamilyDraftClient
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly OnboardingOptions _draftOptions;
    private readonly IOpenAiChatCompletionSender _sender;
    private readonly ILogger<OpenAiFamilyDraftClient> _logger;

    public OpenAiFamilyDraftClient(
        HttpClient http,
        OpenAiOptions options,
        OnboardingOptions draftOptions,
        IOpenAiChatCompletionSender sender,
        ILogger<OpenAiFamilyDraftClient> logger)
    {
        _http = http;
        _options = options;
        _draftOptions = draftOptions;
        _sender = sender;
        _logger = logger;
    }

    public async Task<FamilyDraftResult> DraftTaxonomy(FamilyDraftPrompt prompt, CancellationToken ct)
    {
        var messages = FamilyDraftPromptBuilder.Build(prompt);
        var body = FamilyDraftRequestBuilder.Build(_draftOptions.DraftModel, _draftOptions.DraftReasoningEffort, messages);
        var completion = await _sender.Send(_http, DraftSenderOptions(), OpenAiUsagePurpose.OnboardingDraft, body, ct);
        var parsed = FamilyDraftResponseParser.Parse(completion.Content);

        _logger.LogInformation(
            "Drafted taxonomy for search term {SearchTerm} using {PromptTokens} prompt + {CompletionTokens} completion tokens (~${CostUsd:F4}).",
            prompt.SearchTerm,
            completion.PromptTokens,
            completion.CompletionTokens,
            completion.CostUsd);

        return new FamilyDraftResult(
            parsed.TaxonomyJson, parsed.DealGroupBy, completion.PromptTokens, completion.CompletionTokens, completion.CostUsd);
    }

    private OpenAiOptions DraftSenderOptions() =>
        _options with { Model = _draftOptions.DraftModel, TimeoutSeconds = _draftOptions.DraftTimeoutSeconds };
}
