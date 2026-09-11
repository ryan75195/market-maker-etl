using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Services;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class ScrapeRunServiceTests
{
    private static readonly ScrapeRunWork Work = new(RunId: 1, JobId: 2, SearchTerm: "ps5");

    [Test]
    public async Task Should_persist_listings_and_complete_the_run()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Arg.Any<CancellationToken>())
            .Returns([new ListingSummary("111111111111", "PS5", 1m, "GBP", "https://x/itm/1", false)]);
        var store = Substitute.For<IScrapeStore>();
        var service = new ScrapeRunService(search, store);

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).UpsertListings(2, Arg.Any<IReadOnlyList<ListingSummary>>(), Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRun(1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().FailRun(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_fail_the_run_when_collection_throws()
    {
        var search = Substitute.For<ISearchPageService>();
        search.Collect("ps5", Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ListingSummary>>(_ => throw new InvalidOperationException("scraper down"));
        var store = Substitute.For<IScrapeStore>();
        var service = new ScrapeRunService(search, store);

        await service.Run(Work, CancellationToken.None);

        await store.Received(1).FailRun(1, "scraper down", Arg.Any<CancellationToken>());
        await store.DidNotReceive().CompleteRun(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
