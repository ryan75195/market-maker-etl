using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed partial class EtlDbContext
{
    private static void ConfigureDealSignals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DealSignalEntity>(entity =>
        {
            entity.ToTable("DealSignals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GroupKeyJson).IsRequired();
            entity.HasIndex(e => new { e.ListingEntityId, e.LandedPrice }).IsUnique();
            entity.HasIndex(e => new { e.ProductFamilyId, e.CreatedUtc });
            entity.HasOne<ListingEntity>()
                .WithMany()
                .HasForeignKey(e => e.ListingEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ProductFamilyEntity>()
                .WithMany()
                .HasForeignKey(e => e.ProductFamilyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TaxonomyVersionEntity>()
                .WithMany()
                .HasForeignKey(e => e.TaxonomyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureClassificationBatchRuns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassificationBatchRunEntity>(entity =>
        {
            entity.ToTable("ClassificationBatchRuns");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RanUtc);
        });
    }
}
