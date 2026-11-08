using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static MarketMakerEtl.Core.ConfigurationValueReader;

namespace MarketMakerEtl.Core;

public static partial class ServiceCollectionExtensions
{
    private static void AddEtlDbContextFactory(this IServiceCollection services, IConfiguration? configuration) =>
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite(BuildDatabaseConnectionString(configuration), SendCollectionsAsOneJsonParameter)
                .AddInterceptors(new SqlitePragmaConnectionInterceptor(BuildBusyTimeoutMs(configuration))));

    private static void SendCollectionsAsOneJsonParameter(
        Microsoft.EntityFrameworkCore.Infrastructure.SqliteDbContextOptionsBuilder sqlite) =>
        sqlite.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter);

    private static int BuildBusyTimeoutMs(IConfiguration? configuration) =>
        ReadInt(configuration, "Database:BusyTimeoutMs", DefaultBusyTimeoutMs);

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
}
