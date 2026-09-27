using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Core;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection AddOpenAiCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IOpenAiUsageStore, OpenAiUsageStore>();
        services.AddSingleton<IOpenAiBudgetService, OpenAiBudgetService>();
        services.AddSingleton<IOpenAiChatCompletionSender, OpenAiChatCompletionSender>();
        return services;
    }
}
