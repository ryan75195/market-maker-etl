using MarketMakerEtl.Core.Models.Classification;
using Microsoft.Extensions.Configuration;
using static MarketMakerEtl.Core.ConfigurationValueReader;

namespace MarketMakerEtl.Core;

public static partial class ServiceCollectionExtensions
{
    private const int DefaultClassifierTickMinutes = 5;
    private const int DefaultClassifierMaxListingsPerTick = 2000;
    private const int DefaultClassifierDegradedAfterFailedBatches = 5;
    private const int DefaultClassifierFailureBackoffBaseMinutes = 5;
    private const int DefaultClassifierFailureBackoffMaxMinutes = 240;
    private const double DefaultClassificationReviewThreshold = 0.9;
    private const string DefaultOpenAiModel = "gpt-6-luna";
    private const string DefaultOpenAiReasoningEffort = "low";
    private const int DefaultOpenAiBatchSize = 25;
    private const int DefaultOpenAiMaxConcurrency = 6;
    private const int DefaultOpenAiTimeoutSeconds = 120;

    private static ClassifierOptions BuildClassifierOptions(IConfiguration? configuration) =>
        new(
            ReadInt(configuration, "Classifier:TickMinutes", DefaultClassifierTickMinutes),
            ReadInt(configuration, "Classifier:MaxListingsPerTick", DefaultClassifierMaxListingsPerTick),
            ReadInt(configuration, "Classifier:DegradedAfterFailedBatches", DefaultClassifierDegradedAfterFailedBatches),
            ReadInt(configuration, "Classifier:FailureBackoffBaseMinutes", DefaultClassifierFailureBackoffBaseMinutes),
            ReadInt(configuration, "Classifier:FailureBackoffMaxMinutes", DefaultClassifierFailureBackoffMaxMinutes));

    private static ClassificationReviewOptions BuildClassificationReviewOptions(IConfiguration? configuration) =>
        new(ReadDouble(configuration, "Classification:ReviewThreshold", DefaultClassificationReviewThreshold));

    private static OpenAiOptions BuildOpenAiOptions(IConfiguration? configuration) =>
        new(
            ReadOptionalString(configuration, "OpenAI:ApiKey") ?? string.Empty,
            ReadString(configuration, "OpenAI:Model", DefaultOpenAiModel),
            ReadString(configuration, "OpenAI:ReasoningEffort", DefaultOpenAiReasoningEffort),
            ReadInt(configuration, "OpenAI:BatchSize", DefaultOpenAiBatchSize),
            ReadInt(configuration, "OpenAI:MaxConcurrency", DefaultOpenAiMaxConcurrency),
            ReadInt(configuration, "OpenAI:TimeoutSeconds", DefaultOpenAiTimeoutSeconds));
}
