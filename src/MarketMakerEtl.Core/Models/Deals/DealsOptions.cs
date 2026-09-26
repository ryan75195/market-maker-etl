namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealsOptions(int TickMinutes, string? WebhookUrl);
