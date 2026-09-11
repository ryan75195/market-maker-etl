namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeClient
{
    Task<string> GetPageHtml(string url, CancellationToken ct);
}
