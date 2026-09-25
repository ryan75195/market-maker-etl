using System.Globalization;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core;

public static class ServiceCollectionExtensions
{
    private const string DefaultBaseUrl = "http://localhost:7126";
    private const string DefaultApiKey = "";
    private const string DefaultContentConnectionString = "UseDevelopmentStorage=true";
    private const string DefaultContainerName = "html";
    private const int DefaultMaxPages = 2;
    private const bool DefaultCollectSold = true;
    private const int DefaultMaxBandsPerDirection = 200;
    private const int DefaultSoldBackfillDays = 30;
    private const int DefaultMaxBackfillItemPageFetches = 400;
    private const int DefaultSearchPageMaxAttempts = 5;
    private const int DefaultSearchPageRetryBaseDelaySeconds = 5;
    private const int DefaultSearchConcurrency = 3;
    private const string DefaultDatabaseFileName = "marketmakeretl.db";
    private const int DefaultTickMinutes = 5;
    private const int DefaultRefreshIntervalHours = 24;
    private const int DefaultMaxConcurrentDetailFetches = 4;
    private const int DefaultMaxDetailFetchesPerRun = 50;
    private const int DefaultMaxDetailFetchAttempts = 3;

    private static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services.AddCoreServicesCore(configuration: null);
    }

    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddCoreServicesCore(configuration);
    }

    private static IServiceCollection AddCoreServicesCore(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        services.AddSingleton(BuildScrapeClientOptions(configuration));
        services.AddSingleton(BuildScrapeContentOptions(configuration));
        services.AddSingleton(BuildScrapeOptions(configuration));
        services.AddSingleton(BuildScheduleOptions(configuration));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(BuildDetailFetchOptions(configuration));
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite(BuildDatabaseConnectionString(configuration)));

        services.AddHttpClient<IScrapeClient, HttpScrapeClient>();
        services.AddSingleton<IScrapeContentStore, BlobScrapeContentStore>();
        services.AddSingleton<IEbaySearchUrlService, EbaySearchUrlService>();
        services.AddSingleton<IEbaySearchUrlService, MercariSearchUrlService>();
        services.AddSingleton<IPriceBandSearchUrlService, MercariSearchUrlService>();
        services.AddSingleton<ISearchPageParser, EbaySearchParser>();
        services.AddSingleton<ISearchPageParser, MercariSearchParser>();
        services.AddSingleton<IItemPageParser, EbayItemPageParserService>();
        services.AddSingleton<IItemPageParser, MercariItemPageParser>();
        services.AddSingleton(sp => new MarketplaceAdapters(
            sp.GetServices<IEbaySearchUrlService>(),
            sp.GetServices<ISearchPageParser>(),
            sp.GetServices<IItemPageParser>()));
        services.AddSingleton<IScrapeRunStateService, ScrapeRunStateService>();
        services.AddSingleton<IScrapeStore, ScrapeStore>();
        services.AddSingleton<IScrapeRunReportStore, ScrapeRunReportStore>();
        services.AddSingleton<IJobStore, JobStore>();
        services.AddSingleton<ICategoryStore, CategoryStore>();
        services.AddSingleton<IItemDetailStore, ItemDetailStore>();
        services.AddSingleton<IListingRawDataStore, ListingRawDataStore>();
        services.AddSingleton<ISearchPageService, SearchPageService>();
        services.AddSingleton<IItemDetailFetchService, ItemDetailFetchService>();
        services.AddSingleton<IScrapeRunService, ScrapeRunService>();
        services.AddSingleton<IListingRefreshService, ListingRefreshService>();
        services.AddSingleton<ISchedulerStateStore, SchedulerStateStore>();
        services.AddSingleton<IJobSchedulingService, JobSchedulingService>();
        services.AddSingleton<IListingRefreshSchedulingService, ListingRefreshSchedulingService>();
        return services;
    }

    private static ScrapeClientOptions BuildScrapeClientOptions(IConfiguration? configuration) =>
        new(
            ReadString(configuration, "Scraper:BaseUrl", DefaultBaseUrl),
            ReadString(configuration, "Scraper:ApiKey", DefaultApiKey),
            DefaultFetchTimeout,
            DefaultPollInterval,
            ReadOptionalString(configuration, "Scraper:SessionReference"));

    private static ScrapeContentOptions BuildScrapeContentOptions(IConfiguration? configuration) =>
        new(
            ReadString(configuration, "ContentStore:ConnectionString", DefaultContentConnectionString),
            ReadString(configuration, "ContentStore:ContainerName", DefaultContainerName));

    private static ScrapeOptions BuildScrapeOptions(IConfiguration? configuration) =>
        new(
            ReadInt(configuration, "Scrape:MaxPages", DefaultMaxPages),
            ReadBool(configuration, "Scrape:CollectSold", DefaultCollectSold),
            ReadInt(configuration, "Scrape:MaxBandsPerDirection", DefaultMaxBandsPerDirection),
            ReadInt(configuration, "Scrape:SoldBackfillDays", DefaultSoldBackfillDays),
            ReadInt(configuration, "Scrape:MaxBackfillItemPageFetches", DefaultMaxBackfillItemPageFetches),
            ReadInt(configuration, "Scrape:SearchPageMaxAttempts", DefaultSearchPageMaxAttempts),
            ReadInt(configuration, "Scrape:SearchPageRetryBaseDelaySeconds", DefaultSearchPageRetryBaseDelaySeconds),
            ReadInt(configuration, "Scrape:SearchConcurrency", DefaultSearchConcurrency));

    private static ScheduleOptions BuildScheduleOptions(IConfiguration? configuration) =>
        new(
            ReadInt(configuration, "Schedule:TickMinutes", DefaultTickMinutes),
            ReadInt(configuration, "Schedule:RefreshIntervalHours", DefaultRefreshIntervalHours));

    private static DetailFetchOptions BuildDetailFetchOptions(IConfiguration? configuration) =>
        new(
            ReadInt(configuration, "Scrape:MaxConcurrentDetailFetches", DefaultMaxConcurrentDetailFetches),
            ReadInt(configuration, "Scrape:MaxDetailFetchesPerRun", DefaultMaxDetailFetchesPerRun),
            ReadInt(configuration, "Scrape:MaxDetailFetchAttempts", DefaultMaxDetailFetchAttempts));

    private static string BuildDatabaseConnectionString(IConfiguration? configuration)
    {
        var configured = configuration?["Database:ConnectionString"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"Data Source={ResolveDefaultDatabasePath()}"
            : configured;
    }

    private static string ResolveDefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarketMakerEtl");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, DefaultDatabaseFileName);
    }

    private static string ReadString(IConfiguration? configuration, string key, string fallback)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? ReadOptionalString(IConfiguration? configuration, string key)
    {
        var value = configuration?[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ReadInt(IConfiguration? configuration, string key, int fallback)
    {
        var value = configuration?[key];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBool(IConfiguration? configuration, string key, bool fallback)
    {
        var value = configuration?[key];
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
