namespace MarketMakerEtl.Core.Models.Classification;

public sealed record OpenAiCompletionResult(string Content, int PromptTokens, int CompletionTokens, decimal CostUsd);
