using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core.Services;

internal static class DealServiceCollectionExtensions
{
    public static IServiceCollection AddDealServices(this IServiceCollection services)
    {
        services.AddSingleton<IDealSignalStore, DealSignalStore>();
        services.AddHttpClient<IDealWebhookClient, HttpDealWebhookClient>();
        services.AddSingleton<IDealSignalService, DealSignalService>();
        return services;
    }
}
