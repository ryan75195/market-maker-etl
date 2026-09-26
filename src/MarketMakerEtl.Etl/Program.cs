using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Etl.Workers;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddHostedService<ScrapeWorker>();
builder.Services.AddHostedService<JobQueueingWorker>();
builder.Services.AddHostedService<ListingRefreshWorker>();
builder.Services.AddHostedService<DetailBacklogWorker>();
builder.Services.AddHostedService<ClassificationWorker>();
builder.Services.AddHostedService<StaleJobMonitorWorker>();
builder.Services.AddHostedService<DealScanWorker>();
var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
    await factory.ApplyMigrations();

    var staleRunRecovery = scope.ServiceProvider.GetRequiredService<IStaleScrapeRunRecoveryService>();
    var failedStaleRuns = await staleRunRecovery.FailRunsLeftRunning(CancellationToken.None);

    if (failedStaleRuns > 0)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Failed {Count} scrape run(s) left Running by a previous ETL process at startup.", failedStaleRuns);
    }
}

await host.RunAsync();
