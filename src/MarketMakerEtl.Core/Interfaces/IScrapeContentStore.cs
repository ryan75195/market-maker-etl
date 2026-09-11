namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeContentStore
{
    Task<string> GetHtml(string blobUri, CancellationToken ct);
}
