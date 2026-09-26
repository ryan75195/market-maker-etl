using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDealWebhookClient
{
    Task Notify(DealWebhookPayload payload, CancellationToken ct);
}
