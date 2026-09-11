using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core;

public static class ServiceCollectionExtensions
{
    private static readonly ScrapeClientOptions DefaultScrapeOptions = new(
        "http://localhost:7126",
        string.Empty,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(5));

    private static readonly ScrapeOptions DefaultScrape = new(MaxPages: 2, CollectSold: true);

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton(DefaultScrapeOptions);
        services.AddSingleton(DefaultScrape);
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite("Data Source=marketmakeretl.db"));

        services.AddHttpClient<IScrapeClient, HttpScrapeClient>();
        services.AddSingleton<IEbaySearchUrlService, EbaySearchUrlService>();
        services.AddSingleton<ISearchPageParser, EbaySearchParser>();
        services.AddSingleton<IScrapeRunStateService, ScrapeRunStateService>();
        services.AddSingleton<IScrapeStore, ScrapeStore>();
        services.AddSingleton<ISearchPageService, SearchPageService>();
        services.AddSingleton<IScrapeRunService, ScrapeRunService>();
        return services;
    }
}
