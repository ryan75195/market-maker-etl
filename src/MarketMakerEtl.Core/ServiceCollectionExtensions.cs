using System.Globalization;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
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
    private const bool DefaultDetailBacklogEnabled = true;
    private const int DefaultDetailBacklogTickMinutes = 5;
    private const int DefaultDetailBacklogMaxFetchesPerTick = 30;
    private const int DefaultDetailBacklogMaxFetchesPerHour = 300;
    private const int DefaultFamilyDetailFetchesPerTick = 300;
    private const string DefaultClassifierBaseUrl = "";
    private const int DefaultClassifierBatchSize = 64;
    private const int DefaultClassifierTickMinutes = 5;
    private const int DefaultClassifierMaxListingsPerTick = 2000;
    private const int DefaultClassifierTimeoutSeconds = 120;
    private const double DefaultClassificationReviewThreshold = 0.9;

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
        var detailFetchOptions = BuildDetailFetchOptions(configuration);
        services.AddSingleton(detailFetchOptions);
        services.AddSingleton(BuildDetailBacklogOptions(configuration, detailFetchOptions.MaxDetailFetchAttempts));
        services.AddSingleton(BuildClassifierOptions(configuration));
        services.AddSingleton(BuildClassificationReviewOptions(configuration));
        services.AddSingleton(PriceGroupOptionsFactory.Build(configuration));
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite(BuildDatabaseConnectionString(configuration)));

        return services.AddCoreDomainServices();
    }

    private static IServiceCollection AddCoreDomainServices(this IServiceCollection services)
    {
        services.AddHttpClient<IScrapeClient, HttpScrapeClient>();
        services.AddHttpClient<IListingClassifierClient, HttpListingClassifierClient>(
            client => client.Timeout = Timeout.InfiniteTimeSpan);
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
        services.AddSingleton<IStaleScrapeRunRecoveryService, StaleScrapeRunRecoveryService>();
        services.AddSingleton<IScrapeRunReportStore, ScrapeRunReportStore>();
        services.AddSingleton<IJobStore, JobStore>();
        services.AddSingleton<ICategoryStore, CategoryStore>();
        services.AddSingleton<IProductFamilyStore, ProductFamilyStore>();
        services.AddSingleton<IItemDetailStore, ItemDetailStore>();
        services.AddSingleton<IListingRawDataStore, ListingRawDataStore>();
        services.AddSingleton<ISearchPageService, SearchPageService>();
        services.AddSingleton<IItemDetailFetchService, ItemDetailFetchService>();
        services.AddSingleton<IScrapeRunService, ScrapeRunService>();
        services.AddSingleton<IListingRefreshService, ListingRefreshService>();
        services.AddSingleton<ISchedulerStateStore, SchedulerStateStore>();
        services.AddSingleton<IJobSchedulingService, JobSchedulingService>();
        services.AddSingleton<IListingRefreshSchedulingService, ListingRefreshSchedulingService>();
        services.AddSingleton<IDetailBacklogService, DetailBacklogService>();
        services.AddSingleton<IListingClassificationStore, ListingClassificationStore>();
        services.AddSingleton<IListingClassificationService, ListingClassificationService>();
        services.AddSingleton<IClassificationReviewStore, ClassificationReviewStore>();
        services.AddSingleton<IPriceGroupListingStore, PriceGroupListingStore>();
        services.AddSingleton<IPriceGroupQueryService, PriceGroupQueryService>();
        return services.AddCoreHealthServices();
    }

    private static IServiceCollection AddCoreHealthServices(this IServiceCollection services)
    {
        services.AddSingleton<IJobHealthService, JobHealthService>();
        services.AddSingleton<IFamilyBacklogHealthService, FamilyBacklogHealthService>();
        services.AddSingleton<ISystemHealthService, SystemHealthService>();
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

    private static DetailBacklogOptions BuildDetailBacklogOptions(
        IConfiguration? configuration, int maxDetailFetchAttempts) =>
        new(
            ReadBool(configuration, "DetailBacklog:Enabled", DefaultDetailBacklogEnabled),
            ReadInt(configuration, "DetailBacklog:TickMinutes", DefaultDetailBacklogTickMinutes),
            ReadInt(configuration, "DetailBacklog:MaxFetchesPerTick", DefaultDetailBacklogMaxFetchesPerTick),
            ReadInt(configuration, "DetailBacklog:MaxFetchesPerHour", DefaultDetailBacklogMaxFetchesPerHour),
            maxDetailFetchAttempts,
            ReadInt(configuration, "Scrape:FamilyDetailFetchesPerTick", DefaultFamilyDetailFetchesPerTick));

    private static ClassifierOptions BuildClassifierOptions(IConfiguration? configuration) =>
        new(
            ReadString(configuration, "Classifier:BaseUrl", DefaultClassifierBaseUrl),
            ReadInt(configuration, "Classifier:BatchSize", DefaultClassifierBatchSize),
            ReadInt(configuration, "Classifier:TickMinutes", DefaultClassifierTickMinutes),
            ReadInt(configuration, "Classifier:MaxListingsPerTick", DefaultClassifierMaxListingsPerTick),
            ReadInt(configuration, "Classifier:TimeoutSeconds", DefaultClassifierTimeoutSeconds));

    private static ClassificationReviewOptions BuildClassificationReviewOptions(IConfiguration? configuration) =>
        new(ReadDouble(configuration, "Classification:ReviewThreshold", DefaultClassificationReviewThreshold));

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

    private static double ReadDouble(IConfiguration? configuration, string key, double fallback)
    {
        var value = configuration?[key];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
