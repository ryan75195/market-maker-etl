namespace MarketMakerEtl.Core.Models.Health;

public sealed record LlmHealthView(
    string Model,
    bool HasApiKey,
    int LastHourSucceeded,
    int LastHourFailed,
    bool Degraded);
