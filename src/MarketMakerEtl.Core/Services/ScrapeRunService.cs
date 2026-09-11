using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class ScrapeRunService : IScrapeRunService
{
    private readonly ISearchPageService _search;
    private readonly IScrapeStore _store;

    public ScrapeRunService(ISearchPageService search, IScrapeStore store)
    {
        _search = search;
        _store = store;
    }

    public async Task Run(ScrapeRunWork work, CancellationToken ct)
    {
        try
        {
            var listings = await _search.Collect(work.SearchTerm, ct);
            await _store.UpsertListings(work.JobId, listings, ct);
            await _store.CompleteRun(work.RunId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.FailRun(work.RunId, ex.Message, ct);
        }
    }
}
