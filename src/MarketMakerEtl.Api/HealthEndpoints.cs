using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Api;

public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", async (ISystemHealthService health, CancellationToken ct) =>
            Results.Ok(await health.GetHealth(ct)));

        return app;
    }
}
