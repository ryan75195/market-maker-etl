using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core;

public static class ServiceCollectionExtensions
{
    private static readonly ScrapeClientOptions DefaultScrapeOptions = new(
        "http://localhost:7126",
        string.Empty,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(5));

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton(DefaultScrapeOptions);
        services.AddHttpClient<IScrapeClient, HttpScrapeClient>();
        services.AddSingleton<IEbaySearchUrlService, EbaySearchUrlService>();
        services.AddSingleton<ISearchPageParser, EbaySearchParser>();
        return services;
    }
}
