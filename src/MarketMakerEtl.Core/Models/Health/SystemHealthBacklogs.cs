namespace MarketMakerEtl.Core.Models.Health;

public sealed record SystemHealthBacklogs(IReadOnlyList<ClassificationBacklogView> Classification, int PendingDetailFetch);
