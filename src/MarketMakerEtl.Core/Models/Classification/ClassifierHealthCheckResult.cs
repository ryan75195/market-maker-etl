namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifierHealthCheckResult(string BaseUrl, bool Reachable, IReadOnlyList<string> LoadedModels);
