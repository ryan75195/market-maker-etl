namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifierOptions(
    string BaseUrl,
    int BatchSize,
    int TickMinutes,
    int MaxListingsPerTick,
    int TimeoutSeconds);
