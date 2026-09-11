using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public static class DatabaseMigrationExtensions
{
    public static Task ApplyMigrations(
        this IDbContextFactory<EtlDbContext> factory,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
