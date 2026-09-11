using Azure.Storage.Blobs;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class BlobScrapeContentStore : IScrapeContentStore
{
    private readonly ScrapeContentOptions _options;

    public BlobScrapeContentStore(ScrapeContentOptions options)
    {
        _options = options;
    }

    public async Task<string> GetHtml(string blobUri, CancellationToken ct)
    {
        var blobName = BuildBlobName(blobUri);
        var container = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        var download = await container.GetBlobClient(blobName).DownloadContentAsync(ct);
        return download.Value.Content.ToString();
    }

    public string BuildBlobName(string blobUri)
    {
        var path = new Uri(blobUri, UriKind.Absolute).AbsolutePath;
        var marker = $"/{_options.ContainerName}/";
        var index = path.IndexOf(marker, StringComparison.Ordinal);

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Blob uri '{blobUri}' does not point into the '{_options.ContainerName}' container");
        }

        return Uri.UnescapeDataString(path[(index + marker.Length)..]);
    }
}
