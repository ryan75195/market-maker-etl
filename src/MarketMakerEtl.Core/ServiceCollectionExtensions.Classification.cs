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
    private const string LunaModel = "gpt-6-luna";
    private const decimal DefaultLunaInputPerMillionUsd = 0.10m;
    private const decimal DefaultLunaOutputPerMillionUsd = 0.50m;
    private const string SolModel = "gpt-6-sol";
    private const decimal DefaultSolInputPerMillionUsd = 2.0m;
    private const decimal DefaultSolOutputPerMillionUsd = 10.0m;
    private const decimal DefaultFallbackInputPerMillionUsd = DefaultSolInputPerMillionUsd;
    private const decimal DefaultFallbackOutputPerMillionUsd = DefaultSolOutputPerMillionUsd;
    private const decimal DefaultMonthlyBudgetUsd = 20m;

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

    private static OpenAiPricingOptions BuildOpenAiPricingOptions(IConfiguration? configuration)
    {
        var modelPrices = new Dictionary<string, OpenAiModelPricing>(StringComparer.OrdinalIgnoreCase)
        {
            [LunaModel] = ReadModelPricing(
                configuration, LunaModel, DefaultLunaInputPerMillionUsd, DefaultLunaOutputPerMillionUsd),
            [SolModel] = ReadModelPricing(
                configuration, SolModel, DefaultSolInputPerMillionUsd, DefaultSolOutputPerMillionUsd)
        };

        var defaultPrice = ReadModelPricing(
            configuration, "Default", DefaultFallbackInputPerMillionUsd, DefaultFallbackOutputPerMillionUsd);

        return new OpenAiPricingOptions(modelPrices, defaultPrice);
    }

    private static OpenAiModelPricing ReadModelPricing(
        IConfiguration? configuration, string model, decimal defaultInputPerMillionUsd, decimal defaultOutputPerMillionUsd) =>
        new(
            ReadDecimal(configuration, $"OpenAI:Pricing:{model}:InputPerMillionUsd", defaultInputPerMillionUsd),
            ReadDecimal(configuration, $"OpenAI:Pricing:{model}:OutputPerMillionUsd", defaultOutputPerMillionUsd));

    private static OpenAiBudgetOptions BuildOpenAiBudgetOptions(IConfiguration? configuration) =>
        new(ReadDecimal(configuration, "OpenAI:MonthlyBudgetUsd", DefaultMonthlyBudgetUsd));
}
