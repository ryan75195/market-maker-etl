using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Etl.Workers;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddHostedService<ScrapeWorker>();
var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
    await factory.ApplyMigrations();
}

await host.RunAsync();
