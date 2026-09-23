namespace MarketMakerEtl.Core.Models.Jobs;

public sealed record CategoryView(int Id, string Name, bool IsEnabled, DateTime CreatedUtc);
