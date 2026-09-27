using MarketMakerEtl.Core.Models.Onboarding;
using Microsoft.Extensions.Configuration;
using static MarketMakerEtl.Core.ConfigurationValueReader;

namespace MarketMakerEtl.Core;

public static partial class ServiceCollectionExtensions
{
    private const string DefaultDraftModel = "gpt-6-sol";
    private const string DefaultDraftReasoningEffort = "medium";
    private const double DefaultDraftInputCostPerMillionUsd = 2.0;
    private const double DefaultDraftOutputCostPerMillionUsd = 10.0;
    private const int DefaultMaxSampleListings = 60;
    private const int DefaultMaxDescriptionChars = 500;
    private const int DefaultDraftTimeoutSeconds = 180;

    private static OnboardingOptions BuildOnboardingOptions(IConfiguration? configuration) =>
        new(
            ReadString(configuration, "OpenAI:DraftModel", DefaultDraftModel),
            ReadString(configuration, "OpenAI:DraftReasoningEffort", DefaultDraftReasoningEffort),
            ReadDecimal(configuration, "OpenAI:DraftInputCostPerMillionUsd", (decimal)DefaultDraftInputCostPerMillionUsd),
            ReadDecimal(configuration, "OpenAI:DraftOutputCostPerMillionUsd", (decimal)DefaultDraftOutputCostPerMillionUsd),
            ReadInt(configuration, "Onboarding:MaxSampleListings", DefaultMaxSampleListings),
            ReadInt(configuration, "Onboarding:MaxDescriptionChars", DefaultMaxDescriptionChars),
            ReadInt(configuration, "OpenAI:DraftTimeoutSeconds", DefaultDraftTimeoutSeconds));
}
