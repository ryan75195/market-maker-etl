using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed partial class EtlDbContext
{
    private static void ConfigureTrades(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeEntity>(entity =>
        {
            entity.ToTable("Trades");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedUtc);
            entity.HasIndex(e => e.ProductFamilyId);
            entity.HasOne<DealSignalEntity>()
                .WithMany()
                .HasForeignKey(e => e.DealSignalId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ListingEntity>()
                .WithMany()
                .HasForeignKey(e => e.ListingEntityId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ProductFamilyEntity>()
                .WithMany()
                .HasForeignKey(e => e.ProductFamilyId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
