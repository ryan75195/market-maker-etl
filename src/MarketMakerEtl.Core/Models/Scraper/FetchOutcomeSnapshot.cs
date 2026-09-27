namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record FetchOutcomeSnapshot(
    int SuccessCount,
    int InfrastructureFailureCount,
    int NotFoundFailureCount,
    int OtherFailureCount)
{
    public int TotalCount => SuccessCount + InfrastructureFailureCount + NotFoundFailureCount + OtherFailureCount;
}
