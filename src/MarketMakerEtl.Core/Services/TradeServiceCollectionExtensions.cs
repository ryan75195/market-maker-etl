using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core.Services;

internal static class TradeServiceCollectionExtensions
{
    public static IServiceCollection AddTradeServices(this IServiceCollection services)
    {
        services.AddSingleton<ITradeStore, TradeStore>();
        return services;
    }
}
