using MarketMakerEtl.Core;
using MarketMakerEtl.Etl.Workers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCoreServices();
builder.Services.AddHostedService<ScrapeWorker>();
await builder.Build().RunAsync();
