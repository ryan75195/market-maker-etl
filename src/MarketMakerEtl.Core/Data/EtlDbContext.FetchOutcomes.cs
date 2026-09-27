using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed partial class EtlDbContext
{
    private static void ConfigureFetchOutcomeBuckets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FetchOutcomeBucketEntity>(entity =>
        {
            entity.ToTable("FetchOutcomeBuckets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => new { e.BucketStartUtc, e.Kind }).IsUnique();
        });
    }
}
