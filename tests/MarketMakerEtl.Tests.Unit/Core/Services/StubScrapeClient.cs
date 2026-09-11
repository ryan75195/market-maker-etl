using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

internal sealed class StubScrapeClient : IScrapeClient
{
    private readonly Dictionary<string, string> _pages;
    private readonly List<string> _requestedUrls = [];

    public StubScrapeClient(IReadOnlyDictionary<string, string> pages)
    {
        _pages = new Dictionary<string, string>(pages, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> RequestedUrls => _requestedUrls;

    public Task<string> GetPageHtml(string url, CancellationToken ct)
    {
        _requestedUrls.Add(url);
        return Task.FromResult(_pages[url]);
    }
}
